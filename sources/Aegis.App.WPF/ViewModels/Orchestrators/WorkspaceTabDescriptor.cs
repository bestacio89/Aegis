using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace Aegis.Wpf.ViewModels.Orchestrators
{
    public sealed record WorkspaceTabDescriptor(
      string Name, string Glyph, ICommand SelectCommand);
}
