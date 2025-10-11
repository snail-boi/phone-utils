using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace phone_utils
{
    internal class Debugger
    {
        public static void show (string message)
        {
            if (MainWindow.debugmode) Debug.WriteLine(message);
        }
    }
}
