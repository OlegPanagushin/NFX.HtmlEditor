using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using NFX.CodeAnalysis;

namespace NFX.Markup
{
  internal class DteHelper
  {
    public static OutputWindowPane GetOutputWindow(SVsServiceProvider sVsServiceProvider)
    {
      var dte = (DTE)sVsServiceProvider.GetService(typeof(DTE));
      var window = dte.Windows.Item(Constants.vsWindowKindOutput);
      var ow = (OutputWindow)window.Object;

      for (uint i = 1; i <= ow.OutputWindowPanes.Count; i++)
      {
        if (ow.OutputWindowPanes.Item(i).Name.Equals("NfxPane", StringComparison.CurrentCultureIgnoreCase))
        {
          return ow.OutputWindowPanes.Item(i);
        }
      }
      return ow.OutputWindowPanes.Add("NfxPane");
    }
  }
  internal static class TaskManager
  {
    private static ErrorListProvider _errorListProvider;

    public static void Init(SVsServiceProvider serviceProvider)
    {
      if (_errorListProvider == null)
      _errorListProvider = new ErrorListProvider(serviceProvider);
    }

    public static void AddError(Message message)
    {
      AddTask(message, TaskErrorCategory.Error);
    }

    public static void AddWarning(Message message)
    {
      AddTask(message, TaskErrorCategory.Warning);
    }

    public static void AddMessage(Message message)
    {
      AddTask(message, TaskErrorCategory.Message);
    }

    public static void Refresh()
    {
      _errorListProvider.Tasks.Clear();
    }

    private static void AddTask(Message message, TaskErrorCategory category)
    {
      _errorListProvider.Tasks.Add(new ErrorTask
      {
        Category = TaskCategory.User,
        ErrorCategory = category,
        Text = message.ToString(),
          Column = message.Position.ColNumber,
          Line = message.Position.LineNumber         
      });
    }
  }
}
