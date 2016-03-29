using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using NFX.CodeAnalysis;
using NFX.CodeAnalysis.Laconfig;
using NFX.CodeAnalysis.Source;
using NFX.Environment;

namespace NFX
{
  [ContentType("NfxLaconfig")]
  [TagType(typeof(IClassificationTag))]
  [TagType(typeof(IErrorTag))]
  [Export(typeof(ITaggerProvider))]
  internal sealed class LaconicTagProvider : ITaggerProvider
  {
    [Export]
    [BaseDefinition("code")]
    [Name("NfxLaconfig")]
    internal static ContentTypeDefinition NfxContentType { get; set; }

    [Export]
    [FileExtension(".laconf")]
    [ContentType("NfxLaconfig")]
    internal static FileExtensionToContentTypeDefinition NfxFileType { get; set; }

    [Import]
    internal SVsServiceProvider ServiceProvider { get; set; }

    [Import]
    internal IClassificationTypeRegistryService ClassificationTypeRegistry { get; set; }

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
      return new LaconicClassifier(ClassificationTypeRegistry, ServiceProvider) as ITagger<T>;
    }
  }


  internal sealed class LaconicClassifier : ITagger<IClassificationTag>, ITagger<IErrorTag>
  {
    readonly IDictionary<NfxTokenTypes, IClassificationType> _nfxTypes;
    readonly OutputWindowPane _outputWindow;

    object updateLock = new object();

    internal LaconicClassifier(
      IClassificationTypeRegistryService typeService,
      SVsServiceProvider sVsServiceProvider)
    {
      _outputWindow = DteHelper.GetOutputWindow(sVsServiceProvider);

      _nfxTypes = new Dictionary<NfxTokenTypes, IClassificationType>
      {
        [NfxTokenTypes.Laconf] = typeService.GetClassificationType(Consts.LaconfTokenName),
        [NfxTokenTypes.Expression] = typeService.GetClassificationType(Consts.ExpressionTokenName),
        [NfxTokenTypes.Statement] = typeService.GetClassificationType(Consts.ExpressionTokenName),
        [NfxTokenTypes.ExpressionBrace] = typeService.GetClassificationType(Consts.ExpressionBraceTokenName),
        [NfxTokenTypes.KeyWord] = typeService.GetClassificationType(Consts.KeyWordTokenName),
        [NfxTokenTypes.Error] = typeService.GetClassificationType(Consts.ErrorTokenName),
        [NfxTokenTypes.Brace] = typeService.GetClassificationType(Consts.BraceTokenName),
        [NfxTokenTypes.Literal] = typeService.GetClassificationType(Consts.LiteralTokenName),
        [NfxTokenTypes.Comment] = typeService.GetClassificationType(Consts.CommentTokenName),
        [NfxTokenTypes.Special] = typeService.GetClassificationType(Consts.SpecialTokenName),
        [NfxTokenTypes.Area] = typeService.GetClassificationType(Consts.AreaTokenName),
        [NfxTokenTypes.ExpressionArea] = typeService.GetClassificationType(Consts.ExpressionAreaTokenName),
        [NfxTokenTypes.StatementArea] = typeService.GetClassificationType(Consts.StatementAreaTokenName),
      };
    }

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    private ITextSnapshot _snapshot;
    private List<ITagSpan<IClassificationTag>> _oldtags;
    private static List<ITagSpan<IErrorTag>> _errorTags;

    IEnumerable<ITagSpan<IErrorTag>> ITagger<IErrorTag>.GetTags(NormalizedSnapshotSpanCollection spans)
    {

      if (spans.Count > 0)
      {
        var newSpanshot = spans[0].Snapshot;
        if (_snapshot != newSpanshot)
        {
          _snapshot = newSpanshot;

          lock (updateLock)
          {
            TagsChanged?.Invoke(this,
              new SnapshotSpanEventArgs(new SnapshotSpan(newSpanshot, 0, newSpanshot.Length)));
          }
        }
      }
      return _errorTags ?? new List<ITagSpan<IErrorTag>>();

    }
    /// <summary>
    /// Search the given span for any instances of classified tags
    /// </summary>
    public IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
      var tags = new List<ITagSpan<IClassificationTag>>();

      if (spans.Count < 1)
        return tags;
      var newSpanshot = spans[0].Snapshot;
      if (_snapshot == newSpanshot)
        return _oldtags;

      _snapshot = newSpanshot;

      var sb = new StringBuilder();
      for (var i = 0; i < newSpanshot.Length; i++)
      {
        sb.Append(newSpanshot[i]);
      }

      var text = sb.ToString();
      var errorTags = GetLaconicTags(ref tags, text, _outputWindow, newSpanshot, _nfxTypes);
      lock (updateLock)
      {
        _errorTags = errorTags;
      }
      SynchronousUpdate(_snapshot);

      _oldtags = tags;
      return tags;
    }

    void SynchronousUpdate(ITextSnapshot snapshotSpan)
    {
      lock (updateLock)
      {
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshotSpan, 0, snapshotSpan.Length)));
      }
    }

    internal static List<ITagSpan<IErrorTag>> GetLaconicTags(
      ref List<ITagSpan<IClassificationTag>> classifierTags,
      string src,
      OutputWindowPane outputWindow,
      ITextSnapshot snapshot,
      IDictionary<NfxTokenTypes, IClassificationType> nfxTypes)
    {
      var ml = new MessageList();
      var lxr = new LaconfigLexer(new StringSource(src), ml);
      var cfg = new LaconicConfiguration();
      var ctx = new LaconfigData(cfg);
      var p = new LaconfigParser(ctx, lxr, ml);
      p.Parse();
      var errorTags = new List<ITagSpan<IErrorTag>>();
      foreach (var message in ml)
      {
        outputWindow.OutputString($"{message}{System.Environment.NewLine}");
        var start = message.Token.StartPosition.CharNumber > 4 ? message.Token.StartPosition.CharNumber - 5 : 0;
        var length = src.Length - start > 10 ? 10 : src.Length - start;
        errorTags.Add(CreateTagSpan(start, length, snapshot));
      }
      for (var i = 0; i < lxr.Tokens.Count; i++)
      {
        NfxTokenTypes? curType = null;
        var token = lxr.Tokens[i];
        if (token.IsComment)
          curType = NfxTokenTypes.Comment;
        else if (token.IsIdentifier)
          curType = NfxTokenTypes.KeyWord;
        else if (token.IsSymbol || token.IsOperator)
          curType = NfxTokenTypes.Brace;
        else if (token.IsLiteral)
          curType = NfxTokenTypes.Literal;

        if (curType.HasValue)
          classifierTags.Add(CreateTagSpan(token.StartPosition.CharNumber - 1, token.Text.Length, nfxTypes[curType.Value], snapshot));
      }
      return errorTags;
    }

    internal static TagSpan<IClassificationTag> CreateTagSpan(int startIndex, int length, IClassificationType type, ITextSnapshot snapshot)
    {
      var tokenSpan = new SnapshotSpan(snapshot, new Span(startIndex, length));
      return
        new TagSpan<IClassificationTag>(tokenSpan,
          new ClassificationTag(type));
    }

    internal static TagSpan<IErrorTag> CreateTagSpan(int startIndex, int length, ITextSnapshot snapshot)
    {
      var tokenSpan = new SnapshotSpan(snapshot, new Span(startIndex, length));
      return
        new TagSpan<IErrorTag>(tokenSpan, new ErrorTag());
    }

    public const string LACONFIG_START = "#<laconf>";
    public const string LACONFIG_END = "#</laconf>";
  }
}
