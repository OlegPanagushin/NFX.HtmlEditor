using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using EnvDTE;
using NFX.CodeAnalysis;
using NFX.Environment;
using NFX.CodeAnalysis.Source;
using NFX.CodeAnalysis.Laconfig;
using NFX.CodeAnalysis.CSharp;

namespace NFX.Markup
{
  internal static class Parser
  {
    internal static void FindPropTags(ITextSnapshot snapshot, ITextBufferFactoryService bufferFactoryService,
     List<ITagSpan<IClassificationTag>> tags, IBufferTagAggregatorFactoryService tagAggregatorFactoryService,
     IContentType contetType, string textSpan, int bufferStartPosition)
    {
      var buffer = bufferFactoryService.CreateTextBuffer(textSpan,
                  contetType);
      var snapshotSpan = new SnapshotSpan(buffer.CurrentSnapshot, new Span(0, textSpan.Length));
      var ns =
        new NormalizedSnapshotSpanCollection(new List<SnapshotSpan>
        {
                    snapshotSpan
        });

      var t = tagAggregatorFactoryService.CreateTagAggregator<IClassificationTag>(buffer);
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
              new SnapshotSpan(snapshot, bufferStartPosition + anchor.Start, anchor.Length + (contetType.TypeName == "css" ? 0 : 1)),
              mappingTagSpan.Tag));
        }
      }
    }

    internal static void FindAdditionalsTokens(List<ITagSpan<IClassificationTag>> tags, IClassificationType type, ITextSnapshot snapshot, string text, int start, int length,
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
          Find(word, tags, type, snapshot, j, additionalTokens);
          word = new StringBuilder();
        }
        if (word.Length > 0 && j + 1 == o)
          Find(word, tags, type, snapshot, j, additionalTokens);
        j++;
      }
    }

    internal static void Find(StringBuilder word, List<ITagSpan<IClassificationTag>> tags, IClassificationType type, ITextSnapshot snapshot,
     int currenPosition, params string[] additionalTokens)
    {
      if (ContextCSharpTokens.Any(word.Compare))
      {
        var w = word.ToString();
        tags.Add(CreateTagSpan(currenPosition - w.Length, w.Length, type, snapshot));
      }
      if (additionalTokens != null && additionalTokens.Any(word.Compare))
      {
        var w = word.ToString();
        tags.Add(CreateTagSpan(currenPosition - w.Length, w.Length, type, snapshot));
      }
    }
    internal static List<ITagSpan<IErrorTag>> GetLaconicTags(
      ref List<ITagSpan<IClassificationTag>> classifierTags,
      string src,
      TaskManager taskManager,
      ITextSnapshot snapshot,
      IDictionary<NfxTokenTypes, IClassificationType> nfxTypes,
      int startPosition = 0)
    {
      var ml = new MessageList();
      var lxr = new LaconfigLexer(new StringSource(src), ml);
      var cfg = new LaconicConfiguration();
      var ctx = new LaconfigData(cfg);
      var p = new LaconfigParser(ctx, lxr, ml);
      p.Parse();
      var errorTags = new List<ITagSpan<IErrorTag>>();
      taskManager.Refresh();
      foreach (var message in ml)
      {
        //outputWindow.OutputString($"{message}{System.Environment.NewLine}");
        taskManager.AddError(message);
        var start = message.Token == null ?    0:
          message.Token.StartPosition.CharNumber > 4 
            ? message.Token.StartPosition.CharNumber - 5 : 0;

        var length = message.Token == null ? src.Length - 1 :
          src.Length - start > 10 ? 10 : src.Length - start;
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
          classifierTags.Add(CreateTagSpan(startPosition + token.StartPosition.CharNumber - 1, token.Text.Length, nfxTypes[curType.Value], snapshot));
      }
      return errorTags;
    }
    internal static void FindCSharpTokens(List<ITagSpan<IClassificationTag>> tags, IDictionary<NfxTokenTypes, IClassificationType> types, ITextSnapshot snapshot, string text, int sourceStart, int length)
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
          tags.Add(Parser.CreateTagSpan(sourceStart + token.StartPosition.CharNumber - 1, token.Text.Length, types[curType.Value], snapshot));
      }
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

    public static List<string> ContextCSharpTokens = new List<string>
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