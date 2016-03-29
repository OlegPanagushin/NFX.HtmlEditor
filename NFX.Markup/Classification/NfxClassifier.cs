using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using NFX.CodeAnalysis;
using NFX.CodeAnalysis.CSharp;
using NFX.CodeAnalysis.Laconfig;
using NFX.CodeAnalysis.Source;
using NFX.Environment;

using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace NFX.Classification
{
  [Export(typeof(ITaggerProvider))]
  [ContentType(Consts.Nfx)]
  [TagType(typeof(IClassificationTag))]
  [TagType(typeof(IErrorTag))]
  internal sealed class NfxClassifierProvider : ITaggerProvider
  {
    [Export]
    [BaseDefinition("html")]
    [Name(Consts.Nfx)]
    internal static ContentTypeDefinition NfxContentType { get; set; }

    [Export]
    [FileExtension(".nht")]
    [ContentType(Consts.Nfx)]
    internal static FileExtensionToContentTypeDefinition NfxFileType { get; set; }

    [Import]
    internal IClassificationTypeRegistryService ClassificationTypeRegistry { get; set; }

    [Import]
    internal IBufferTagAggregatorFactoryService TagAggregatorFactoryService { get; set; }

    [Import]
    internal ITextBufferFactoryService BufferFactory { get; set; }

    [Import]
    internal IContentTypeRegistryService ContentTypeRegistryService { get; set; }

    [Import]
    internal SVsServiceProvider ServiceProvider { get; set; }

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
      return new NfxClassifier(ClassificationTypeRegistry, BufferFactory, TagAggregatorFactoryService, ContentTypeRegistryService, ServiceProvider) as ITagger<T>;
    }
  }

  internal sealed class NfxClassifier : ITagger<IClassificationTag>, ITagger<IErrorTag>
  {
    readonly IDictionary<NfxTokenTypes, IClassificationType> _nfxTypes;

    readonly IContentType _cssContentType;
    readonly IContentType _javaScripContentType;
    readonly ITextBufferFactoryService _bufferFactory;
    readonly IBufferTagAggregatorFactoryService _tagAggregatorFactoryService;
    readonly OutputWindowPane _outputWindow;

    object updateLock = new object();

    internal NfxClassifier(
      IClassificationTypeRegistryService typeService,
      ITextBufferFactoryService bufferFactory,
      IBufferTagAggregatorFactoryService tagAggregatorFactoryService,
      IContentTypeRegistryService contentTypeRegistryService,
      SVsServiceProvider sVsServiceProvider)
    {
      _bufferFactory = bufferFactory;
      _tagAggregatorFactoryService = tagAggregatorFactoryService;

      _cssContentType = contentTypeRegistryService.GetContentType("css");
      _javaScripContentType = contentTypeRegistryService.GetContentType("JavaScript");

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

      var k = 0;
      while (k < text.Length)        //#[] - area
      {
        if (text[k] == '#')
        {
          if (text.Length - k > 1 &&
              text[k + 1] == '[' &&
              text[k] == '#' &&
              (k == 0 || text[k - 1] != '#'))
          {
            var o = k + 1;
            while (o < text.Length)
            {
              if (text[o] == ']')
              {
                tags.Add(LaconicClassifier.CreateTagSpan(k, 2, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //#[ 
                tags.Add(LaconicClassifier.CreateTagSpan(o, 1, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //]

                var j = k + 2;
                //FindCSharpTokens(tags, text, j, o - j);
                FindAdditionalsTokens(tags, text, j, o - j, "render", "class");
                tags.Add(LaconicClassifier.CreateTagSpan(j, o - j, _nfxTypes[NfxTokenTypes.Area], _snapshot));
                break;
              }
              o++;
            }
          }
        }

        if (text[k] == '@')    //@[statement]
        {
          if (text.Length - k > 1 &&
              text[k] == '@' &&
              text[k + 1] == '[' &&
              (k == 0 || text[k - 1] != '@'))
          {
            var o = k + 1;
            while (o < text.Length)
            {
              if (text[o] == ']')
              {
                tags.Add(LaconicClassifier.CreateTagSpan(k, 2, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //#[ 
                tags.Add(LaconicClassifier.CreateTagSpan(o, 1, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //]

                var j = k + 2;
                FindCSharpTokens(tags, text, j, o - j);
                FindAdditionalsTokens(tags, text, j, o - j);
                tags.Add(LaconicClassifier.CreateTagSpan(j, o - j, _nfxTypes[NfxTokenTypes.StatementArea], _snapshot));
                break;
              }
              o++;
            }
          }
        }

        if (text[k] == '?') //Experession ?[]
        {
          if (text.Length - k > 1
            && text[k + 1] == '[' &&
            text[k] == '?'
            && (k == 0 || text[k - 1] != '?'))
          {
            var o = k + 1;
            while (o < text.Length)
            {
              if (text[o] == ']')
              {
                tags.Add(LaconicClassifier.CreateTagSpan(k, 2, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //?[
                tags.Add(LaconicClassifier.CreateTagSpan(o, 1, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //]

                var j = k + 2;

                FindCSharpTokens(tags, text, j, o - j);
                FindAdditionalsTokens(tags, text, j, o - j);
                tags.Add(LaconicClassifier.CreateTagSpan(j, o - j, _nfxTypes[NfxTokenTypes.ExpressionArea], _snapshot));
                break;
              }
              o++;
            }
          }
        }

        if (text[k] == '#' && text[k] == '#' && (k == 0 || text[k - 1] != '#'))     //class section
        {
          if (text.Length - k > CLASS_AREA_FULL.Length &&
              text.Substring(k, CLASS_AREA_FULL.Length) == CLASS_AREA_FULL) //#[class]
          {
            var j = k + CLASS_AREA_FULL.Length;
            var o = text.IndexOf("#[", j, StringComparison.OrdinalIgnoreCase);
            FindCSharpTokens(tags, text, j, o > -1 ? o - j : text.Length - j);
            FindAdditionalsTokens(tags, text, j, o > -1 ? o - j : text.Length - j);
          }
        }

        if (text[k] == '#' && text[k] == '#' && (k == 0 || text[k - 1] != '#'))       //laconic config
        {
          if (text.Length - k > LACONFIG_START.Length &&
              text.Substring(k, LACONFIG_START.Length) == LACONFIG_START) //#<laconf>
          {
            var o = k + 1;
            while (o < text.Length)
            {
              if (text.Length - o > LACONFIG_END.Length && text[o] == '#' &&
                  text.Substring(o, LACONFIG_END.Length) == LACONFIG_END) //#<laconf>
              {
                tags.Add(LaconicClassifier.CreateTagSpan(k, LACONFIG_START.Length, _nfxTypes[NfxTokenTypes.Laconf], _snapshot)); //#<laconf>
                tags.Add(LaconicClassifier.CreateTagSpan(o, LACONFIG_END.Length, _nfxTypes[NfxTokenTypes.Laconf], _snapshot)); //#<laconf>

                var j = k + LACONFIG_START.Length;
                LaconicClassifier.GetLaconicTags(ref tags, text.Substring(j, o - j), _outputWindow, _snapshot, _nfxTypes);
                break;
                //var ml = new MessageList();
                //var lxr = new LaconfigLexer(new StringSource(text.Substring(j, o - j)), ml);
                //var cfg = new LaconicConfiguration();
                //var ctx = new LaconfigData(cfg);
                //var p = new LaconfigParser(ctx, lxr, ml);
                //p.Parse();
                //lock (updateLock)
                //{
                //  _errorTags = new List<ITagSpan<IErrorTag>>();
                //  foreach (var message in ml)
                //  { 
                //    this._outputWindow.OutputString(message + System.Environment.NewLine);
                //    _errorTags.Add(CreateTagSpan(j, o - LACONFIG_END.Length - j));
                //  }
                //}         
                //for (var i = 0; i < lxr.Tokens.Count; i++)
                //{
                //  NfxTokenTypes? curType = null;
                //  var token = lxr.Tokens[i];
                //  if (token.IsComment)
                //    curType = NfxTokenTypes.Comment;
                //  else if (token.IsIdentifier)
                //    curType = NfxTokenTypes.KeyWord;
                //  else if (token.IsSymbol || token.IsOperator)
                //    curType = NfxTokenTypes.Brace;
                //  else if (token.IsLiteral)
                //    curType = NfxTokenTypes.Literal;

                //  if (curType.HasValue)
                //    tags.Add(CreateTagSpan(j + token.StartPosition.CharNumber - 1, token.Text.Length, curType.Value));
                //}
              }
              o++;
            }
          }
        }

        if (text[k] == '<')                //styles
        {
          if (text.Length - k > STYLE_START.Length &&
              text.Substring(k, STYLE_START.Length) == STYLE_START)
          {
            var o = k + STYLE_START.Length;
            while (o < text.Length)
            {
              if (text.Length - o > STYLE_END.Length &&
                  text.Substring(o, STYLE_END.Length) == STYLE_END)
              {
                var tt = text.Substring(k + STYLE_START.Length, o - k - STYLE_START.Length);

                FindPropTags(tags, _cssContentType, tt, k);
                break;
              }
              o++;
            }
          }
        }

        if (text[k] == '<')                            //scripts
        {
          if (text.Length - k > SCRIPT_START.Length &&
              text.Substring(k, SCRIPT_START.Length) == SCRIPT_START)
          {
            var o = k + SCRIPT_START.Length;
            while (o < text.Length)
            {
              if (text.Length - o > SCRIP_END.Length &&
                  text.Substring(o, SCRIP_END.Length) == SCRIP_END)
              {
                var tt = text.Substring(k + SCRIPT_START.Length, o - k - SCRIPT_START.Length);
                FindPropTags(tags, _javaScripContentType, tt, k);
                break;
              }
              o++;
            }
          }
        }

        k++;
      }
      SynchronousUpdate(_snapshot, _oldtags, tags);

      _oldtags = tags;
      return tags;
    }

    void SynchronousUpdate(ITextSnapshot snapshotSpan, List<ITagSpan<IClassificationTag>> oldTags, List<ITagSpan<IClassificationTag>> newTags)
    {
      lock (updateLock)
      {
        //if (_oldtags == null ||
        //newTags.Count != oldTags.Count ||
        //  newTags.Except(oldTags).Any() ||
        //  oldTags.Except(newTags).Any())
        {
          TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshotSpan, 0, snapshotSpan.Length)));
        }
      }
    }

    internal void FindCSharpTokens(List<ITagSpan<IClassificationTag>> tags, string text, int sourceStart, int length)
    {
      var ml = new MessageList();
      var lxr = new CSLexer(new StringSource(text.Substring(sourceStart, length)), ml);
      lxr.AnalyzeAll();
      for (var i = 0; i < lxr.Tokens.Count; i++)
      {
        NfxTokenTypes? curType = null;
        var token = lxr.Tokens[i];
        if (token.IsComment)
          curType = NfxTokenTypes.Comment;
        else if (token.IsKeyword)
          curType = NfxTokenTypes.KeyWord;
        else if (token.IsLiteral)
          curType = NfxTokenTypes.Literal;
        else if (token.IsSymbol || token.IsOperator)
          curType = NfxTokenTypes.Brace;

        if (curType.HasValue)
          tags.Add(LaconicClassifier.CreateTagSpan(sourceStart + token.StartPosition.CharNumber - 1, token.Text.Length, _nfxTypes[curType.Value], _snapshot));
      }
    }

    internal void FindPropTags(List<ITagSpan<IClassificationTag>> tags, IContentType contetType, string textSpan, int bufferStartPosition)
    {
      var buffer = _bufferFactory.CreateTextBuffer(textSpan,
                  contetType);
      var snapshotSpan = new SnapshotSpan(buffer.CurrentSnapshot, new Span(0, textSpan.Length));
      var ns =
        new NormalizedSnapshotSpanCollection(new List<SnapshotSpan>
        {
                    snapshotSpan
        });


      var t = _tagAggregatorFactoryService.CreateTagAggregator<IClassificationTag>(buffer);
      if (t != null)
      {
        var tttt = t.GetTags(ns);
        foreach (var mappingTagSpan in tttt)
        {
          var anchor =
            (SnapshotSpan)mappingTagSpan.Span.GetType()
              .GetField("_anchor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Static)
              .GetValue(mappingTagSpan.Span);
          tags.Add(
            new TagSpan<IClassificationTag>(
              new SnapshotSpan(_snapshot, bufferStartPosition + STYLE_START.Length + anchor.Start, anchor.Length + (contetType.TypeName == "css" ? 0 : 1)),
              mappingTagSpan.Tag));
        }
      }
    }

    internal void FindAdditionalsTokens(List<ITagSpan<IClassificationTag>> tags, string text, int start, int length,
      params string[] additionalTokens)
    {
      var j = start;
      var word = new StringBuilder();
      var o = start + length;
      while (j < o)
      {
        var c = text[j];
        if (char.IsLetter(c))
        {
          word.Append(c);
        }
        else if (word.Length > 0)
        {
          Find(word, tags, j, additionalTokens);
          word = new StringBuilder();
        }
        if (word.Length > 0 && j + 1 == o)
          Find(word, tags, j, additionalTokens);
        j++;
      }
    }

    void Find(StringBuilder word, List<ITagSpan<IClassificationTag>> tags, int j, params string[] additionalTokens)
    {
      if (ContextCSharpTokens.Any(word.Compare))
      {
        var w = word.ToString();
        tags.Add(LaconicClassifier.CreateTagSpan(j - w.Length, w.Length, _nfxTypes[NfxTokenTypes.KeyWord], _snapshot));
      }
      if (additionalTokens != null && additionalTokens.Any(word.Compare))
      {
        var w = word.ToString();
        tags.Add(LaconicClassifier.CreateTagSpan(j - w.Length, w.Length, _nfxTypes[NfxTokenTypes.Special], _snapshot));
      }
    }       

    public const string CONFIG_START = "#<conf>";
    public const string CONFIG_END = "#</conf>";
    public const string LACONFIG_START = "#<laconf>";
    public const string LACONFIG_END = "#</laconf>";
    public const string CLASS_AREA_FULL = "#[class]";
    public const string STYLE_START = "<style>";
    public const string STYLE_END = "</style>";
    public const string SCRIPT_START = "<script>";
    public const string SCRIP_END = "</script>";

    public List<string> ContextCSharpTokens = new List<string>
    {
      "string",
      "get",
      "set",
    };
  }

  public static class Helper
  {
    public static bool Compare(this StringBuilder builder, string value)
    {
      if (builder == null || value == null)
        return false;

      if (builder.Length != value.Length)
        return false;

      for (var i = 0; i < builder.Length; i++)
      {
        if (!builder[i].Equals(value[i]))
          return false;
      }
      return true;
    }
  }
}