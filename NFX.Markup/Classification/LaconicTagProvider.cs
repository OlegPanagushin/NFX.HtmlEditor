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

namespace NFX.Markup
{
	[ContentType("Laconic")]
	[TagType(typeof(IClassificationTag))]
	[TagType(typeof(IErrorTag))]
	[Export(typeof(ITaggerProvider))]
	[Export(typeof(IClassifierProvider))]
	internal sealed class LaconicTagProvider : ITaggerProvider, IClassifierProvider
	{
		[Export]
		[BaseDefinition("code")]
		[Name("Laconic")]
		internal static ContentTypeDefinition NfxContentType { get; set; }

		[Export]
		[FileExtension(".laconf")]
		[ContentType("Laconic")]
		internal static FileExtensionToContentTypeDefinition NfxFileType { get; set; }

		[Import]
		internal SVsServiceProvider ServiceProvider { get; set; }

		[Import]
		internal IClassificationTypeRegistryService ClassificationTypeRegistry { get; set; }

		public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
		{
      TaskManager.Init(ServiceProvider);
			return new LaconicClassifier(ClassificationTypeRegistry, ServiceProvider) as ITagger<T>;
		}
		public IClassifier GetClassifier(ITextBuffer buffer)
		{
      TaskManager.Init(ServiceProvider);
			return buffer.Properties.GetOrCreateSingletonProperty<LaconicClassifier>(delegate { return new LaconicClassifier(ClassificationTypeRegistry, ServiceProvider); });
		}
	}


	internal sealed class LaconicClassifier : ITagger<IClassificationTag>, ITagger<IErrorTag>, IClassifier
	{
		readonly IDictionary<NfxTokenTypes, IClassificationType> _nfxTypes;
		readonly OutputWindowPane _outputWindow;

		object updateLock = new object();

		internal LaconicClassifier(
			IClassificationTypeRegistryService typeService,
			SVsServiceProvider sVsServiceProvider)
		{
			_outputWindow = DteHelper.GetOutputWindow(sVsServiceProvider);
			TaskManager.Init(sVsServiceProvider);

			_nfxTypes = new Dictionary<NfxTokenTypes, IClassificationType>();
			_nfxTypes.Add(NfxTokenTypes.Laconf, typeService.GetClassificationType(Consts.LaconfTokenName));
			_nfxTypes.Add(NfxTokenTypes.Expression, typeService.GetClassificationType(Consts.ExpressionTokenName));
			_nfxTypes.Add(NfxTokenTypes.Statement, typeService.GetClassificationType(Consts.ExpressionTokenName));
			_nfxTypes.Add(NfxTokenTypes.ExpressionBrace, typeService.GetClassificationType(Consts.ExpressionBraceTokenName));
			_nfxTypes.Add(NfxTokenTypes.KeyWord, typeService.GetClassificationType(Consts.KeyWordTokenName));
			_nfxTypes.Add(NfxTokenTypes.Error, typeService.GetClassificationType(Consts.ErrorTokenName));
			_nfxTypes.Add(NfxTokenTypes.Brace, typeService.GetClassificationType(Consts.BraceTokenName));
			_nfxTypes.Add(NfxTokenTypes.Literal, typeService.GetClassificationType(Consts.LiteralTokenName));
			_nfxTypes.Add(NfxTokenTypes.Comment, typeService.GetClassificationType(Consts.CommentTokenName));
			_nfxTypes.Add(NfxTokenTypes.Special, typeService.GetClassificationType(Consts.SpecialTokenName));
			_nfxTypes.Add(NfxTokenTypes.Area, typeService.GetClassificationType(Consts.AreaTokenName));
			_nfxTypes.Add(NfxTokenTypes.ExpressionArea, typeService.GetClassificationType(Consts.ExpressionAreaTokenName));
			_nfxTypes.Add(NfxTokenTypes.StatementArea, typeService.GetClassificationType(Consts.StatementAreaTokenName));
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
            var t = TagsChanged;
            if (t != null)
              TagsChanged.Invoke(this,
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

			//TODO Fix this bullshit
			for (var i = 0; i < newSpanshot.Length; i++)
			{
				sb.Append(newSpanshot[i]);
			}

			var text = sb.ToString();
			var errorTags = Parser.GetLaconicTags(ref tags, text, newSpanshot, _nfxTypes);
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
        var t = TagsChanged;
        if (t != null)
          TagsChanged.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshotSpan, 0, snapshotSpan.Length)));
			}
		}



		public const string LACONFIG_START = "#<laconf>";
		public const string LACONFIG_END = "#</laconf>";

		public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

		private List<ClassificationSpan> listOfSpans = new List<ClassificationSpan>();
		public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
		{
			return listOfSpans;
		}
	}
}
