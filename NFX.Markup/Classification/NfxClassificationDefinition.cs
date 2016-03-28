using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace NFX.Classification
{
  internal static class NfxClassificationDefinition
  {
    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.LaconfTokenName)]
    internal static ClassificationTypeDefinition laconfToken = null;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.ExpressionTokenName)]
    internal static ClassificationTypeDefinition expressionToken = null;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.ExpressionBraceTokenName)]
    internal static ClassificationTypeDefinition expressionBraceToken = null;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.KeyWordTokenName)]
    internal static ClassificationTypeDefinition keyWordToken = null;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.ErrorTokenName)]
    internal static ClassificationTypeDefinition errorToken = null;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.BraceTokenName)]
    internal static ClassificationTypeDefinition braceToken = null;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.LiteralTokenName)]
    internal static ClassificationTypeDefinition literalToken = null;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.CommentTokenName)]
    internal static ClassificationTypeDefinition commentToken = null;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Consts.SpecialTokenName)]
    internal static ClassificationTypeDefinition specialToken = null;
  }
}

internal static class Consts
{
  internal const string Nfx = "Nfx";

  internal const string LaconfTokenName = "LaconfToken";
  internal const string ExpressionTokenName = "ExpressionToken";
  internal const string ExpressionBraceTokenName = "ExpressionBraceTokenName";
  internal const string KeyWordTokenName = "KeyWordTokenName";
  internal const string ErrorTokenName = "ErrorTokenName";
  internal const string BraceTokenName = "BraceTokenName";
  internal const string LiteralTokenName = "LiteralTokenName";
  internal const string CommentTokenName = "CommentTokenName";
  internal const string SpecialTokenName = "SpecialTokenName";
}