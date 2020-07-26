using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.ServiceProcess;

namespace NC24_Service
{
    [RunInstaller(true)]
    public partial class Installer1 : System.Configuration.Install.Installer
    {
        private ServiceProcessInstaller _srvprocess;
        private ServiceInstaller _srvinstaller;

        public Installer1()
        {
            InitializeComponent();
            _srvprocess = new ServiceProcessInstaller();
            _srvprocess.Account = ServiceAccount.LocalSystem;

            _srvinstaller = new ServiceInstaller();
            _srvinstaller.ServiceName = "NC24_Service";
            _srvinstaller.Description = "Servicio de procesamiento con Caja de Valores - COA";
            Installers.Add(_srvprocess);
            Installers.Add(_srvinstaller);
        }
    }
}
