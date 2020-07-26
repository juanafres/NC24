using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using System.Net;
using System.IO;
using System.Web.Script.Serialization;

namespace NC24_Service.WSConsumer
{
    class RestClient
    {
        /// <summary>
        /// Enviar solicitud de ingreso de orden (para alta de Tipo C o V) hacia CV.
        /// Se arma un JSon para el request a partir de una clase ya definida.
        /// Se actualizan 2 variables publicas que se van a utilizar en Service1:
        /// _resultado: Diccionario string, string donde esta toda la respuesta de CV.
        /// _descripcionError: Obtenido desde CV.
        /// </summary>
        /// <param name="TransaccionLocal">Clase a partir de la cual se arma el JSON</param>
        /// <returns>true: Se dio de alta la orden - false: No.</returns>
        public bool IngresarOrden(AccesoDatos.ConexionDB2.Transaccion TransaccionLocal)
        {
            // Limpio el diccionario de respuesta para luego poder agregar orden
            _resultado.Clear();

            //ToDo: Verificar armado de mensaje
            IngresarOrdenJSON _requestJson = new IngresarOrdenJSON();
            _requestJson.agente = TransaccionLocal.TramaMensaje.Substring(0, 4);
            _requestJson.idOrigen = TransaccionLocal.NumeroSecuencia;
            _requestJson.fechaOrigen = TransaccionLocal.FechaAlta;
            _requestJson.ejecucion = "SINCRONICA";
            _requestJson.tipo = TransaccionLocal.TramaMensaje.Substring(4, 1);
            _requestJson.instrumento = TransaccionLocal.TramaMensaje.Substring(6, 5);
            _requestJson.cantidad = TransaccionLocal.TramaMensaje.Substring(11, 11);
            _requestJson.precio = TransaccionLocal.TramaMensaje.Substring(22, 8);
            _requestJson.formaOp = TransaccionLocal.TramaMensaje.Substring(5, 1);
            _requestJson.tipoVenc = "72";
            _requestJson.comitente = TransaccionLocal.TramaMensaje.Substring(36, 4);
            _requestJson.cuit = "20999999994";
            _requestJson.codigoAgente = TransaccionLocal.TramaMensaje.Substring(0, 4);

            string strJson = new JavaScriptSerializer().Serialize(_requestJson);
            // Envio hacia Senebi 
            string _response = RealizarEnvio(strJson, "ingresarOrden");

            // Lectura de JSON
            _resultado = ProcesarRespuesta(_response);

            string _codRespuesta = _resultado.FirstOrDefault(x => x.Key == "resultado").Value;

            if (_codRespuesta.ToUpper() == "OK")
            {
                _descripcionError = string.Empty;
                return true;
            }
            else
            {
                _descripcionError = _resultado.FirstOrDefault(x => x.Key == "codigo").Value;
                _descripcionError += " - ";
                _descripcionError += _resultado.FirstOrDefault(x => x.Key == "observaciones").Value;
                return false;
            }
        }

        /// <summary>
        /// Se consultan todas las operaciones de un agente entre un rango de numeros de orden
        /// Se arma un JSon para el request a partir de una clase ya definida.
        /// Se actualizan 2 variables publicas que se van a utilizar en Service1:
        /// _resultado: Diccionario string, string donde esta toda la respuesta de CV.
        /// _descripcionError: Obtenido desde CV.
        /// </summary>
        /// <param name="TransaccionLocal">Clase que se utiliza para armar el JSON</param>
        /// <returns>true: Se obtuvieron operaciones - false: No.</returns>
        public bool ConsultarOperaciones(AccesoDatos.ConexionDB2.Transaccion TransaccionLocal)
        {
            // Limpio el diccionario de respuesta para luego poder agregar orden
            _resultado.Clear();

            ConsultarOrdenJSON _request = new ConsultarOrdenJSON();
            
            _request.codigoAgente = TransaccionLocal.TramaMensaje.Substring(10,4);
            _request.ordenDesde = TransaccionLocal.TramaMensaje.Substring(2, 6);
            _request.ordenHasta = (Convert.ToDecimal(TransaccionLocal.TramaMensaje.Substring(2, 6)) + 10).ToString();

            string strJson = new JavaScriptSerializer().Serialize(_request);
            // Envio hacia Senebi
            string _response = RealizarEnvio(strJson, "consultarOrdenesBilateral");

            // Lectura de JSON
            _resultado = ProcesarRespuesta(_response);

            string _codRespuesta = _resultado.FirstOrDefault(x => x.Key == "resultado").Value;

            if (_codRespuesta.ToUpper() == "OK")
            {
                _descripcionError = string.Empty;
                return true;
            }
            else
            {
                _descripcionError = _resultado.FirstOrDefault(x => x.Key == "observaciones").Value;
                return false;
            }
        }

        /// <summary>
        /// Realiza el envio del request al Servicio Rest
        /// Tipo de envio: Post
        /// Tipo de Datos: JSON
        /// TimeOut: Por default se va a tomar 1 minuto
        /// </summary>
        /// <param name="JsonRequest">JSON a enviar</param>
        /// <param name="Metodo">Metodo de WS a ejecutar</param>
        /// <returns>JSON de respuesta</returns>
        private string RealizarEnvio(string JsonRequest, string Metodo)
        {
            var postString = JsonRequest;
            byte[] data = UTF8Encoding.UTF8.GetBytes(postString);
            string respuesta;

            string _servidor = ConfigurationManager.AppSettings["Servidor"];
            _servidor += "/" + Metodo;
            System.Net.HttpWebRequest webReq = (HttpWebRequest)System.Net.WebRequest.Create(_servidor);
            webReq.Timeout = 60 * 1000; // Time Out de 1 minuto
            webReq.Method = "POST";
            webReq.ContentType = "application/json; charset=utf-8";
            webReq.ContentLength = data.Length;

            Stream stream = webReq.GetRequestStream();
            stream.Write(data, 0, data.Length);
            stream.Close();

            using (WebResponse response = webReq.GetResponse()) 
            {
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    respuesta = reader.ReadToEnd();
                }
            }

            return respuesta;
        }

        /// <summary>
        /// Transforma respuesta JSON a Diccionario: string, string para poder parsearla
        /// </summary>
        /// <param name="Respuesta">JSON recibido por Senebi</param>
        /// <returns>Diccionario string string con JSON de respuesta</returns>
        private Dictionary<string, string> ProcesarRespuesta(string Respuesta)
        {
            JavaScriptSerializer _jscript = new JavaScriptSerializer();
            var _result = _jscript.Deserialize<Dictionary<string, string>>(Respuesta);

            if (_result != null)
            {
                return _result;
            }

            return new Dictionary<string, string>() ;
        }

        public string _descripcionError;
        public Dictionary<string, string> _resultado; 
        class IngresarOrdenJSON
        {
            public string agente { get; set; }
            public string idOrigen { get; set; }
            public string fechaOrigen { get; set; }
            public string ejecucion { get; set; }
            public string tipo { get; set; }
            public string instrumento { get; set; }
            public string cantidad { get; set; }
            public string precio { get; set; }
            public string formaOp { get; set; }
            public string tipoVenc { get; set; }
            public string comitente { get; set; }
            public string cuit { get; set; }
            public string codigoAgente { get; set; }
        }
        class ConsultarOrdenJSON
        {
            public string ordenDesde {get;set;}
            public string ordenHasta {get;set;}
            public string codigoAgente { get; set; }
        }

    }
}
