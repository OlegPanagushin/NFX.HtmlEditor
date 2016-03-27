﻿using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace NFX.Classification
{
  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = Consts.Nfx)]
  [Name(Consts.LaconfTokenName)]
  [UserVisible(false)]
  [Order(Before = Priority.High)]
  internal sealed class NfxLaconfToken : ClassificationFormatDefinition
  {
    public NfxLaconfToken()
    {
      DisplayName = "Laconfig";
      ForegroundColor = Colors.Red;
      BackgroundColor = Colors.Yellow;
    }
  }

  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = Consts.Nfx)]
  [Name(Consts.ExpressionTokenName)]
  [UserVisible(false)]
  [Order(Before = Priority.High)]
  internal sealed class ExpressionToken : ClassificationFormatDefinition
  {
    public ExpressionToken()
    {
      DisplayName = "Expression";
      ForegroundColor = Colors.DarkViolet;
    }
  }

  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = Consts.Nfx)]
  [Name(Consts.ExpressionBraceTokenName)]
  [UserVisible(false)]
  [Order(Before = Priority.High)]
  internal sealed class ExpressionBraceToken : ClassificationFormatDefinition
  {
    public ExpressionBraceToken()
    {
      DisplayName = "ExpressionBrace";
      ForegroundColor = Colors.DarkViolet;
      BackgroundColor = Colors.Yellow;
    }
  }

  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = Consts.Nfx)]
  [Name(Consts.KeyWordTokenName)]
  [UserVisible(false)]
  [Order(Before = Priority.High, After = Priority.High)]
  internal sealed class KeyWordBraceToken : ClassificationFormatDefinition
  {
    public KeyWordBraceToken()
    {
      DisplayName = "KeyWordBrace";
      ForegroundColor = Colors.Blue;
    }
  }


  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = Consts.Nfx)]
  [Name(Consts.ErrorTokenName)]
  [UserVisible(false)]
  [Order(Before = Priority.High)]
  internal sealed class ErrorToken : ClassificationFormatDefinition
  {
    public ErrorToken()
    {
      DisplayName = "Error";
      IsItalic = true;
      IsBold = true;
    }
  }

  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = Consts.Nfx)]
  [Name(Consts.BraceTokenName)]
  [UserVisible(false)]
  [Order(Before = Priority.High)]
  internal sealed class BraceToken : ClassificationFormatDefinition
  {
    public BraceToken()
    {
      DisplayName = "Brace";
      ForegroundColor = Colors.Chocolate;
    }
  }

  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = Consts.Nfx)]
  [Name(Consts.LiteralTokenName)]
  [UserVisible(false)]
  [Order(Before = Priority.High)]
  internal sealed class LiteralToken : ClassificationFormatDefinition
  {
    public LiteralToken()
    {
      DisplayName = "Literal";
      ForegroundColor = Colors.Red;
    }
  }

  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = Consts.Nfx)]
  [Name(Consts.CommentTokenName)]
  [UserVisible(false)]
  [Order(Before = Priority.High)]
  internal sealed class CommentToken : ClassificationFormatDefinition
  {
    public CommentToken()
    {
      DisplayName = "Comment";
      ForegroundColor = Colors.Green;
    }
  }
}
