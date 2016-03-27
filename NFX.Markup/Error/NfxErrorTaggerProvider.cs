using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using NFX.CodeAnalysis;
using NFX.CodeAnalysis.Laconfig;
using NFX.CodeAnalysis.Source;
using NFX.Environment;

namespace NFX.Error
{
  [Export(typeof (ITaggerProvider))]
  [ContentType(Consts.Nfx)]
  [TagType(typeof (IErrorTag))]
  internal sealed class NfxErrorTaggerProvider : ITaggerProvider
  {
    [Export]
    [BaseDefinition("code")]
    [Name(Consts.Nfx)]
    internal static ContentTypeDefinition NfxContentType { get; set; }

    [Export]
    [FileExtension(".nht")]
    [ContentType(Consts.Nfx)]
    internal static FileExtensionToContentTypeDefinition NfxFileType { get; set; }        

    [Import]
    internal IBufferTagAggregatorFactoryService TagAggregatorFactoryService { get; set; }

    [Import]
    internal ITextBufferFactoryService BufferFactory { get; set; }

    [Import]
    internal IContentTypeRegistryService ContentTypeRegistryService { get; set; }

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
    {
      return new NfxErrorTagger(BufferFactory, TagAggregatorFactoryService, ContentTypeRegistryService) as ITagger<T>;  
    }
  }

  internal sealed class NfxErrorTagger : ITagger<IErrorTag>
  {
    readonly IContentType _cssContentType;
    readonly IContentType _javaScripContentType;
    readonly ITextBufferFactoryService _bufferFactory;
    readonly IBufferTagAggregatorFactoryService _tagAggregatorFactoryService;

    object updateLock = new object();

    internal NfxErrorTagger(
      ITextBufferFactoryService bufferFactory,
      IBufferTagAggregatorFactoryService tagAggregatorFactoryService,
      IContentTypeRegistryService contentTypeRegistryService)
    {
      _bufferFactory = bufferFactory;
      _tagAggregatorFactoryService = tagAggregatorFactoryService;

      _cssContentType = contentTypeRegistryService.GetContentType("css");
      _javaScripContentType = contentTypeRegistryService.GetContentType("JavaScript");
    }

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    private ITextSnapshot _snapshot;
    private List<ITagSpan<IErrorTag>> _oldtags;

    /// <summary>
    /// Search the given span for any instances of classified tags
    /// </summary>
    public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
      var tags = new List<ITagSpan<IErrorTag>>();

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
      while (k < text.Length)
      {
        if (text[k] == '#')
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
                var j = k + LACONFIG_START.Length;

                var ml = new MessageList();
                var lxr = new LaconfigLexer(new StringSource(text.Substring(j, o - j)), ml);

                var cfg = new LaconicConfiguration();
                var ctx = new LaconfigData(cfg);
                var p = new LaconfigParser(ctx, lxr, ml);
                p.Parse();
                foreach (var message in ml)
                {
                  //TODO messages to output
                  tags.Add(CreateTagSpan(j, o - LACONFIG_END.Length - j));
                }
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

    void SynchronousUpdate(ITextSnapshot snapshotSpan, List<ITagSpan<IErrorTag>> oldTags, List<ITagSpan<IErrorTag>> newTags)
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
    internal void FindPropTags(List<ITagSpan<IErrorTag>> tags, IContentType contetType, string textSpan, int bufferStartPosition)
    {
      var buffer = _bufferFactory.CreateTextBuffer(textSpan,
                  contetType);
      var snapshotSpan = new SnapshotSpan(buffer.CurrentSnapshot, new Span(0, textSpan.Length));
      var ns =
        new NormalizedSnapshotSpanCollection(new List<SnapshotSpan>
        {
                    snapshotSpan
        });


      var t = _tagAggregatorFactoryService.CreateTagAggregator<IErrorTag>(buffer);
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
            new TagSpan<IErrorTag>(
              new SnapshotSpan(_snapshot, bufferStartPosition + STYLE_START.Length + anchor.Start, anchor.Length + (contetType.TypeName == "css" ? 0 : 1)),
              mappingTagSpan.Tag));
        }
      }
    }

    internal TagSpan<IErrorTag> CreateTagSpan(int startIndex, int length)
    {
      var tokenSpan = new SnapshotSpan(_snapshot, new Span(startIndex, length));
      return
        new TagSpan<IErrorTag>(tokenSpan, new ErrorTag());
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