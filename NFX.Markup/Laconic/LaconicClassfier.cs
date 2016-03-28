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

namespace NFX.Laconic
{
  [ContentType("NfxLaconfig")]
  [TagType(typeof(IClassificationTag))]
  [TagType(typeof(IErrorTag))]
  [Export(typeof(ITaggerProvider))]
  internal sealed class LaconicClassfier : ITaggerProvider
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
                                               
    readonly OutputWindowPane outputWindow;

    object updateLock = new object();

    readonly DTE _dte;

    internal LaconicClassifier(
      IClassificationTypeRegistryService typeService,
      SVsServiceProvider sVsServiceProvider)
    {                                                                                 

      _dte = (DTE)sVsServiceProvider.GetService(typeof(DTE));

      var window = _dte.Windows.Item(EnvDTE.Constants.vsWindowKindOutput);
      var ow = (OutputWindow)window.Object;

      for (uint i = 1; i <= ow.OutputWindowPanes.Count; i++)
      {
        if (ow.OutputWindowPanes.Item(i).Name.Equals("NfxPane", StringComparison.CurrentCultureIgnoreCase))
        {
          outputWindow = ow.OutputWindowPanes.Item(i);
          break;
        }
      }

      if (outputWindow == null)
        outputWindow = ow.OutputWindowPanes.Add("NfxPane");

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
      var ml = new MessageList();
      var lxr = new LaconfigLexer(new StringSource(text), ml);
      var cfg = new LaconicConfiguration();
      var ctx = new LaconfigData(cfg);
      var p = new LaconfigParser(ctx, lxr, ml);
      p.Parse();
      lock (updateLock)
      {
        _errorTags = new List<ITagSpan<IErrorTag>>();
        foreach (var message in ml)
        {
          outputWindow.OutputString(message + System.Environment.NewLine);
          _errorTags.Add(CreateTagSpan(message.Token.StartPosition.CharNumber > 4 ? message.Token.StartPosition.CharNumber - 5 : 0, 10));
        }
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
          tags.Add(CreateTagSpan(token.StartPosition.CharNumber - 1, token.Text.Length, curType.Value));
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

    internal TagSpan<IClassificationTag> CreateTagSpan(int startIndex, int length, NfxTokenTypes type)
    {
      var tokenSpan = new SnapshotSpan(_snapshot, new Span(startIndex, length));
      return
        new TagSpan<IClassificationTag>(tokenSpan,
          new ClassificationTag(_nfxTypes[type]));
    }

    internal TagSpan<IErrorTag> CreateTagSpan(int startIndex, int length)
    {
      var tokenSpan = new SnapshotSpan(_snapshot, new Span(startIndex, length));
      return
        new TagSpan<IErrorTag>(tokenSpan, new ErrorTag());
    }
                                                  
    public const string LACONFIG_START = "#<laconf>";
    public const string LACONFIG_END = "#</laconf>";   
  }     
}
