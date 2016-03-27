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

namespace NFX.Classification
{
  [Export(typeof (ITaggerProvider))]
  [ContentType(Consts.Nfx)]
  [TagType(typeof (IClassificationTag))]
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

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {                                                                                    
      return new NfxClassifier(ClassificationTypeRegistry, BufferFactory, TagAggregatorFactoryService, ContentTypeRegistryService) as ITagger<T>;
    }           
  }

  internal sealed class NfxClassifier : ITagger<IClassificationTag>
  {
    readonly IDictionary<NfxTokenTypes, IClassificationType> _nfxTypes;

    readonly IContentType _cssContentType;
    readonly IContentType _javaScripContentType;
    readonly ITextBufferFactoryService _bufferFactory;
    readonly IBufferTagAggregatorFactoryService _tagAggregatorFactoryService;

    object updateLock = new object();

    internal NfxClassifier(
      IClassificationTypeRegistryService typeService,
      ITextBufferFactoryService bufferFactory,
      IBufferTagAggregatorFactoryService tagAggregatorFactoryService,
      IContentTypeRegistryService contentTypeRegistryService)
    {
      _bufferFactory = bufferFactory;
      _tagAggregatorFactoryService = tagAggregatorFactoryService;

      _cssContentType = contentTypeRegistryService.GetContentType("css");
      _javaScripContentType = contentTypeRegistryService.GetContentType("JavaScript");

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
      };
    }

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged ;

    private ITextSnapshot _snapshot;
    private List<ITagSpan<IClassificationTag>> _oldtags;
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
      //TODO Переделать на получение зон
      while (k < text.Length)
      {
        if (text[k] == '@' || text[k] == '#')
        {
          if (text.Length - k > 1 &&
              ((text[k] == '@' && (k == 0 || text[k - 1] != '@')) ||
               (text[k] == '#' && (k == 0 || text[k - 1] != '#'))))
          {
            if (text[k + 1] == '[')
            {
              var o = k + 1;
              while (o < text.Length)
              {
                if (text[o] == ']')
                {
                  tags.Add(CreateTagSpan(k, 2, NfxTokenTypes.ExpressionBrace)); // @[ OR #[ 
                  tags.Add(CreateTagSpan(o, 1, NfxTokenTypes.ExpressionBrace)); //]

                  var j = k + 2;
                  FindCSharpTokens(tags, text, j, o - j);
                  FindCustomTokens(tags, text, j, o - j);
                  break;
                }
                o++;
              }
            }
          }
        }

        if (text[k] == '?')
        {
          if (text.Length - k > 1 && text[k + 1] == '[' &&
            text[k] == '?' && (k == 0 || text[k - 1] != '?'))
          {
            var o = k + 1;
            while (o < text.Length)
            {
              if (text[o] == ']')
              {
                tags.Add(CreateTagSpan(k, 2, NfxTokenTypes.ExpressionBrace)); //?[
                tags.Add(CreateTagSpan(o, 1, NfxTokenTypes.ExpressionBrace)); //]

                var j = k + 2;

                FindCSharpTokens(tags, text, j, o - j);
                FindCustomTokens(tags, text, j, o - j);
                break;
              }
              o++;
            }
          }
        }

        if (text[k] == '#' && text[k] == '#' && (k == 0 || text[k - 1] != '#'))
        {
          if (text.Length - k > CLASS_AREA_FULL.Length &&
              text.Substring(k, CLASS_AREA_FULL.Length) == CLASS_AREA_FULL) //#[class]
          {
            var j = k + CLASS_AREA_FULL.Length;
            var o = text.IndexOf("#[", j, StringComparison.OrdinalIgnoreCase);            
            FindCSharpTokens(tags, text, j, o > -1 ? o : text.Length - j);
                FindCustomTokens(tags, text, j, o > -1 ? o : text.Length - j);
          }
        }

        if (text[k] == '#' && text[k] == '#' && (k == 0 || text[k - 1] != '#'))
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
                tags.Add(CreateTagSpan(k, LACONFIG_START.Length, NfxTokenTypes.Laconf)); //#<laconf>
                tags.Add(CreateTagSpan(o, LACONFIG_END.Length, NfxTokenTypes.Laconf)); //#<laconf>

                var j = k + LACONFIG_START.Length;

                var ml = new MessageList();
                var lxr = new LaconfigLexer(new StringSource(text.Substring(j, o - j)), ml);
                lxr.AnalyzeAll();
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
                    tags.Add(CreateTagSpan(j + token.StartPosition.CharNumber - 1, token.Text.Length, curType.Value));
                }
              }
              o++;
            }
          }
        }    

        if (text[k] == '<')
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
              }
              o++;
            }
          }
        }

        if (text[k] == '<')
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

    internal void FindCSharpTokens(List<ITagSpan<IClassificationTag>> tags, string text, int sourceStart,int length)
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
        else if (token.IsSymbol)
          curType = NfxTokenTypes.Brace;

        if (curType.HasValue)
          tags.Add(CreateTagSpan(sourceStart + token.StartPosition.CharNumber - 1, token.Text.Length, curType.Value));
      }
    }

    internal void FindPropTags(List<ITagSpan<IClassificationTag>>  tags, IContentType contetType, string textSpan, int bufferStartPosition)
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
              new SnapshotSpan(_snapshot, bufferStartPosition + STYLE_START.Length + anchor.Start, anchor.Length +( contetType.TypeName == "css" ? 0 : 1)),
              mappingTagSpan.Tag));
        }
      }     
    }

    internal void FindCustomTokens(List<ITagSpan<IClassificationTag>> tags, string text, int start, int length)
    {
      var j = start;
      var word = new StringBuilder();
      while (j < start + length)
      {
        var c = text[j];
        if (char.IsLetter(c))
        {
          word.Append(c);
        }
        else if (word.Length > 0)
        {
          if (CustomTokens.Any(x => word.Compare(x)))
          {
            var w = word.ToString();
            tags.Add(CreateTagSpan(j - w.Length, w.Length, NfxTokenTypes.KeyWord));
          }
          word = new StringBuilder();
        }
        j++;
      }
    }

    internal TagSpan<IClassificationTag> CreateTagSpan(int startIndex, int length, NfxTokenTypes type)
    {
      var tokenSpan = new SnapshotSpan(_snapshot, new Span(startIndex, length));
      return
        new TagSpan<IClassificationTag>(tokenSpan,
          new ClassificationTag(_nfxTypes[type]));
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

    public List<string> CustomTokens = new List<string>
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