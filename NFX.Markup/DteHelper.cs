using System;                  
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace NFX
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
}
