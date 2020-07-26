using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.IO;

namespace NC24_Service
{
    public partial class Service1 : ServiceBase
    {
        bool _processStart;
        string _pathLogs;

        /// <summary>
        /// Constructor
        /// </summary>
        public Service1()
        {
            InitializeComponent();
            this.ServiceName = "NC24_Service";
        }

        /// <summary>
        /// Start de servicio
        /// </summary>
        /// <param name="args">No utilizado</param>
        protected override void OnStart(string[] args)
        {
            StartProcess();
        }
        
        /// <summary>
        /// Proceso inicial de procesamiento. Inicio por Thread
        /// </summary>
        public void StartProcess()
        {
            // Control de procesamiento en true
            _processStart = true;
            _pathLogs = System.Configuration.ConfigurationManager.AppSettings["PathLogs"];

            // Inicio Thread
            System.Threading.Thread _thread = new System.Threading.Thread(new System.Threading.ThreadStart(ProcesarMensajes));
            _thread.Start();
        }
        

        public void ProcesarMensajes()
        {
            string _newArchivo = _pathLogs + "\\NC24Service_Log" + string.Format("{0:yyyyMMdd}", DateTime.Now.Date) + ".txt";
            System.IO.StreamWriter sw = new StreamWriter(_newArchivo, true);

            sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Iniciando procesamiento de mensajes");

            AccesoDatos.ConexionDB2 _conDB2 = new AccesoDatos.ConexionDB2();
            AccesoDatos.ConexionDB2.Transaccion _trx = new AccesoDatos.ConexionDB2.Transaccion();
            WSConsumer.RestClient _restClient = new WSConsumer.RestClient();
            bool _resultadoTrx = false;
            string _mensajeError = string.Empty;

            try
            {
                // Conectar a base DB2
                //_conDB2 = new AccesoDatos.ConexionDB2();
                _conDB2.ConectarBase();
            }
            catch (Exception ex)
            {
                AuditarExcepcion("Sin poder conectar a la base DB2", ex, true);
                sw.Close();
                this.OnStop();
            }

            while (_processStart)
            {
                try
                {
                    sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Obteniendo mensaje...");
                    // Obtener mensaje a procesar
                    _trx = _conDB2.ObtenerProximoMensaje();
                }
                catch (Exception ex)
                {
                    AuditarExcepcion("Error al obtener mensajes de la base", ex, true);
                    _conDB2.DesconectarBase();
                    sw.Close();
                    this.OnStop();
                }

                try
                {
                    sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Mensaje obtenido: " + _trx.CodigoTransaccion.ToUpper());

                    if (_trx.CodigoTransaccion.ToUpper() == "RP03") //Trx: Insertar Orden
                    {
                        // Envio a WS Senebi
                        _resultadoTrx = _restClient.IngresarOrden(_trx);
                        if(!_resultadoTrx)
                            _mensajeError = _restClient._descripcionError;

                        sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Resultado procesamiento: " + _resultadoTrx.ToString());
                    }
                    else if (_trx.CodigoTransaccion.ToUpper() == "RP12") //Trx: Consultar Orden
                    {
                        // Envio a WS Senebi
                        _resultadoTrx = _restClient.ConsultarOperaciones(_trx);
                        if(!_resultadoTrx)
                            _mensajeError = _restClient._descripcionError;

                        sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Resultado procesamiento: " + _resultadoTrx.ToString());
                    }
                    else
                    {
                        //Transaccion no reconocida
                        _resultadoTrx = false;
                        _mensajeError = "No existe operatoria para la transaccion recibida";

                        sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Resultado procesamiento: " + " Operacion invalida");
                    }
                }
                catch (Exception ex)
                {
                    AuditarExcepcion("Problemas de conexion con el WS", ex, true);
                    _conDB2.ActualizarTransaccion(_trx.NumeroSecuencia, false, "Error de conexion con el WS");
                    _conDB2.DesconectarBase();
                    sw.Close();
                    this.OnStop();
                }

                try
                {
                    sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Actualizo transaccion y audito mensaje enviado");
                    //Actualizo estado de transaccion obtenida de la base
                    _conDB2.ActualizarTransaccion(_trx.NumeroSecuencia, _resultadoTrx, _mensajeError);
                    //Audito mensaje como enviado
                    _conDB2.AuditarTransaccion(_trx.NumeroSecuencia, "E", _trx.TramaMensaje);
                }
                catch (Exception ex)
                {
                    AuditarExcepcion("Sin poder actualizar el mensaje enviado", ex, true);
                    _conDB2.DesconectarBase();
                    sw.Close();
                    this.OnStop();
                }

                try
                {
                    // Inserto respuesta de Senebi en Tabla de Operaciones ya sea Ok o no
                    if (_trx.CodigoTransaccion.ToUpper() == "RP03")
                    {
                        sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Actualizo operacion ALTA DE ORDEN");
                        string _codOpe = _restClient._resultado.FirstOrDefault(x => x.Key == "id").Value;
                        string _plazo = _restClient._resultado.FirstOrDefault(x => x.Key == "tipoVenc").Value;
                        _conDB2.ActualizarOperacion(_trx.NumeroSecuencia, _codOpe, _plazo, _mensajeError, _resultadoTrx);
                    }

                    
                    if (_trx.CodigoTransaccion.ToUpper() == "RP12" && _resultadoTrx)
                    {
                        sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Actualizo operaciones CONSULTA DE ORDENES");
                        //ToDo: Leer cantidade operaciones consultadas.
                        //ToDo: Actualizar 1 por 1 en tabla de operaciones.
                    }
                }
                catch (Exception ex)
                {
                    AuditarExcepcion("Sin poder insertar orden", ex, true);
                    _conDB2.DesconectarBase();
                    sw.Close();
                    this.OnStop();
                }
                
                System.Threading.Thread.Sleep(6000);
            }

            _conDB2.DesconectarBase();
            sw.Close();
        }

        /// <summary>
        /// Ingreso de eventos en visor de sucesos
        /// </summary>
        /// <param name="msg">Detalle de donde se genera el evento</param>
        /// <param name="ex">Excepcion</param>
        /// <param name="EsError">Indica si se tiene que grabar el evento como error o Warning</param>
        public void AuditarExcepcion(string msg, Exception ex, bool EsError)
        {
            string _err = GetMensajesEx(ex);

            if(EsError)
                System.Diagnostics.EventLog.WriteEntry("NC24_Service", msg + "\r\n" + "\r\n" + _err, EventLogEntryType.Error);
            else
                System.Diagnostics.EventLog.WriteEntry("NC24_Service", msg + "\r\n" + "\r\n" + _err, EventLogEntryType.Warning);
        }

        /// <summary>
        /// Obtiene detalle de la excepcion
        /// </summary>
        /// <param name="ex">Excepcion obtenida</param>
        /// <returns>Detalle de la execion en formato string</returns>
        private static string GetMensajesEx(Exception ex)
        {
            string _err = ex.Message;
            if (ex.InnerException != null)
            {
                _err += "\r\n";
                _err += GetMensajesEx(ex.InnerException);
            }

            return _err;
        }

        /// <summary>
        /// Envio de mails. Se obtiene server, remitente y destinatarios desde app_config.
        /// </summary>
        /// <param name="asunto">string que viaja en el asunto del mensaje</param>
        /// <param name="body">cuerpo del mensaje de mail</param>
        public void EnviarMail(string asunto, string body)
        {
            try
            {
                string[] _dest = System.Text.RegularExpressions.Regex.Split(System.Configuration.ConfigurationManager.AppSettings["SmtpDestinatarios"], ";");

                for (int intI = 0; intI < _dest.Length; intI++)
                {
                    System.Net.Mail.SmtpClient objMail = new System.Net.Mail.SmtpClient();
                    objMail.Host = System.Configuration.ConfigurationManager.AppSettings["SmtpServer"];
                    objMail.Port = Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings["SmtpPort"]);
                    objMail.EnableSsl = (System.Configuration.ConfigurationManager.AppSettings["SmtpSSL"].ToUpper() == "S");
                    objMail.UseDefaultCredentials = true;
                    System.Net.Mail.MailMessage objMsg = new System.Net.Mail.MailMessage(System.Configuration.ConfigurationManager.AppSettings["SmtpRemitente"], _dest[intI], asunto, body + "<p>&nbsp;</p> <p>&nbsp;</p>");
                    objMsg.IsBodyHtml = true;
                    objMail.Send(objMsg);
                }
            }
            catch (Exception ex)
            {
                AuditarExcepcion("No se pudo enviar el Mail", ex, false);
            }
        }

        /// <summary>
        /// Stop de servicio
        /// </summary>
        protected override void OnStop()
        {
            _processStart = false;
        }
    }
}
