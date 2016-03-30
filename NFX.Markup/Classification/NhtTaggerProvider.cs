using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;    
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace NFX.Markup
{
  [Export(typeof(ITaggerProvider))]
  [ContentType(Consts.Nfx)]
  [TagType(typeof(IClassificationTag))]
  [TagType(typeof(IErrorTag))]
  internal sealed class NhtTaggerProvider : ITaggerProvider
  {
    [Export]
    [BaseDefinition("code")]
    [BaseDefinition("htmlx")]
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
      return new NhtTagger(ClassificationTypeRegistry, BufferFactory, TagAggregatorFactoryService, ContentTypeRegistryService, ServiceProvider) as ITagger<T>;
    }
  }

  internal sealed class NhtTagger : ITagger<IClassificationTag>, ITagger<IErrorTag>
  {
    readonly IDictionary<NfxTokenTypes, IClassificationType> _nfxTypes;

    readonly IContentType _cssContentType;
    readonly IContentType _javaScripContentType;
    readonly ITextBufferFactoryService _bufferFactory;
    readonly IBufferTagAggregatorFactoryService _tagAggregatorFactoryService;
    readonly OutputWindowPane _outputWindow;

    object updateLock = new object();

    internal NhtTagger(
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
      _taskManager = new TaskManager(sVsServiceProvider);

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
    private TaskManager _taskManager;

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
              new SnapshotSpanEventArgs(new SnapshotSpan(_snapshot, 0, _snapshot.Length)));
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
      var errorTags = new List<ITagSpan<IErrorTag>>();

      if (spans.Count < 1)
        return tags;
      var newSpanshot = spans[0].Snapshot;
      if (_snapshot == newSpanshot)
        return _oldtags;

      _snapshot = newSpanshot;

      var sb = new StringBuilder();
      for (var i = 0; i < _snapshot.Length; i++)
      {
        sb.Append(_snapshot[i]);
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
                tags.Add(Parser.CreateTagSpan(k, 2, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //#[ 
                tags.Add(Parser.CreateTagSpan(o, 1, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //]

                var j = k + 2;
                //FindCSharpTokens(tags, text, j, o - j);
                Parser.FindAdditionalsTokens(tags, _nfxTypes[NfxTokenTypes.KeyWord], _snapshot, text, j, o - j, "render", "class");
                tags.Add(Parser.CreateTagSpan(j, o - j, _nfxTypes[NfxTokenTypes.Area], _snapshot));
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
                tags.Add(Parser.CreateTagSpan(k, 2, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //#[ 
                tags.Add(Parser.CreateTagSpan(o, 1, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //]

                var j = k + 2;
                Parser.FindCSharpTokens(tags, _nfxTypes, _snapshot, text, j, o - j);
                Parser.FindAdditionalsTokens(tags, _nfxTypes[NfxTokenTypes.KeyWord], _snapshot, text, j, o - j);
                tags.Add(Parser.CreateTagSpan(j, o - j, _nfxTypes[NfxTokenTypes.StatementArea], _snapshot));
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
                tags.Add(Parser.CreateTagSpan(k, 2, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //?[
                tags.Add(Parser.CreateTagSpan(o, 1, _nfxTypes[NfxTokenTypes.ExpressionBrace], _snapshot)); //]

                var j = k + 2;

                Parser.FindCSharpTokens(tags, _nfxTypes, _snapshot, text, j, o - j);
                Parser.FindAdditionalsTokens(tags, _nfxTypes[NfxTokenTypes.KeyWord], _snapshot, text, j, o - j);
                tags.Add(Parser.CreateTagSpan(j, o - j, _nfxTypes[NfxTokenTypes.ExpressionArea], _snapshot));
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
            Parser.FindCSharpTokens(tags, _nfxTypes, _snapshot, text, j, o > -1 ? o - j : text.Length - j);
            Parser.FindAdditionalsTokens(tags, _nfxTypes[NfxTokenTypes.KeyWord], _snapshot, text, j, o > -1 ? o - j : text.Length - j);
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
                tags.Add(Parser.CreateTagSpan(k, LACONFIG_START.Length, _nfxTypes[NfxTokenTypes.Laconf], _snapshot)); //#<laconf>
                tags.Add(Parser.CreateTagSpan(o, LACONFIG_END.Length, _nfxTypes[NfxTokenTypes.Laconf], _snapshot)); //#<laconf>

                var j = k + LACONFIG_START.Length;
                errorTags = Parser.GetLaconicTags(ref tags, text.Substring(j, o - j), _taskManager, _snapshot, _nfxTypes, j);
                break;
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

                Parser.FindPropTags(_snapshot, _bufferFactory, tags, _tagAggregatorFactoryService, _cssContentType, tt, k + STYLE_START.Length);
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
                Parser.FindPropTags(_snapshot, _bufferFactory, tags, _tagAggregatorFactoryService, _javaScripContentType, tt, k + STYLE_START.Length);
                break;
              }
              o++;
            }
          }
        }

        k++;
      }
      SynchronousUpdate(_snapshot, _oldtags, tags);
      lock (updateLock)
      {
        _errorTags = errorTags;
      }
      _oldtags = tags;
      return tags;
    }

    void SynchronousUpdate(ITextSnapshot snapshotSpan, List<ITagSpan<IClassificationTag>> oldTags, List<ITagSpan<IClassificationTag>> newTags)
    {
      lock (updateLock)
      {
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshotSpan, 0, snapshotSpan.Length)));
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
  }
}
