using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IBM.Data.DB2;
using System.IO;

namespace NC24_Service.AccesoDatos
{
    class ConexionDB2
    {
        /// <summary>
        /// Conecta a la base de datos dejando la misma abierta
        /// </summary>
        public void ConectarBase()
        {
            string _pathLogs = System.Configuration.ConfigurationManager.AppSettings["PathLogs"];
            string _newArchivo = _pathLogs + "\\ConexionDB_Log" + string.Format("{0:yyyyMMdd}", DateTime.Now.Date) + ".txt";
            System.IO.StreamWriter sw = new StreamWriter(_newArchivo, true);

            //ToDo: Logeo para verificar errores - sacar luego de tener ok la comunicacion
            sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Obtengo Conexion string");
            sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " ConnString: " + System.Configuration.ConfigurationManager.ConnectionStrings["DB2Base"].ConnectionString);
            sw.Flush();
            
            _myConn = new DB2Connection(System.Configuration.ConfigurationManager.ConnectionStrings["DB2Base"].ConnectionString);

            //ToDo: Logeo para verificar errores - sacar luego de tener ok la comunicacion
            sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " IDB2Connection inicializada");
            sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Version IBM DB2: " + _myConn.ServerVersion);
            sw.Flush();

            _myConn.Open();

            //ToDo: Logeo para verificar errores - sacar luego de tener ok la comunicacion
            sw.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "-" + " Conexion abierta");
            sw.Close();
        }

        /// <summary>
        /// Desconecta de la base de datos
        /// </summary>
        public void DesconectarBase()
        {
            _myConn.Close();
        }

        /// <summary>
        /// Obtiene los datos necesarios para poder enviar la transaccion a SENEBI
        /// </summary>
        /// <returns>Clase Transaccion, array de string</returns>
        public Transaccion ObtenerProximoMensaje()
        {
            DB2Transaction _trans = _myConn.BeginTransaction();
            DB2Command _myDB2Command = _myConn.CreateCommand();
            DB2DataReader _reader;
            Transaccion _transaccion = new Transaccion();

            // Obtiene 1 transaccion del dia en estado pendiente ordenada por fecha y hora de alta
            // Query de version anterior: SELECT V4CTRX, V4IRED, V4NSEQ, V4DUSU, V4CLAV, V4DATO, V4ESTA, V4MENS, V4FALT, V4HALT, V4IUSR, V4FENV, V4HENV, V4FRTA, V4HRTA
            string _myQuery = "SELECT V4CTRX, V4NSEQ, V4DATO, V4ESTA, V4FALT, V4HALT";
            _myQuery += "FROM CCVREQ ";
            _myQuery += "WHERE (V4FALT = " + Convert.ToDecimal(string.Format("{0:yyyyMMdd}", DateTime.Now)).ToString() + ") AND (V4ESTA = 'P') ";
            _myQuery += "ORDER BY V4FALT, V4HALT FETCH FIRST 1 ROW ONLY";
            
            _myDB2Command.CommandText = _myQuery;
            _myDB2Command.Transaction = _trans;
            _reader = _myDB2Command.ExecuteReader();

            if (_reader.Read())
            {
                _transaccion.CodigoTransaccion = _reader.GetString(0);
                _transaccion.NumeroSecuencia = _reader.GetString(1);
                _transaccion.TramaMensaje = _reader.GetString(2);
                _transaccion.EstadoMensaje = _reader.GetString(3);
                _transaccion.FechaAlta = _reader.GetString(4);
                _transaccion.HoraAlta = _reader.GetString(5);
            }
            _reader.Close();
            _trans.Commit();

            return _transaccion;
        }

        /// <summary>
        /// Actualiza estado y mensaje de una operacion obtenida
        /// </summary>
        /// <param name="NumeroSecuencia">Numero de secuencia de la operacion</param>
        /// <param name="Resultado">P:Procesada - R:Error</param>
        /// <param name="MensajeError">Descripcion del error en caso que corresponda</param>
        public void ActualizarTransaccion(string NumeroSecuencia, bool Resultado, string MensajeError)
        {
            string _estado = string.Empty;
            if (Resultado)
                _estado = "P";
            else
                _estado = "R";

            DB2Transaction _trans = _myConn.BeginTransaction();
            DB2Command _myDB2Command = _myConn.CreateCommand();

            string _myQuery = "UPDATE CCVREQ ";
            _myQuery += "SET V4FENV = ?, V4HENV = ?, V4ESTA = '?', V4MENS = '?' ";
            _myQuery += "WHERE  V4NSEQ = ?";

            _myDB2Command.CommandText = _myQuery;
            _myDB2Command.Parameters.Add("@FechaEnvio", DB2Type.Decimal);
            _myDB2Command.Parameters.Add("@HoraEnvio", DB2Type.Decimal);
            _myDB2Command.Parameters.Add("@Estado", DB2Type.VarChar);
            _myDB2Command.Parameters.Add("@Mensaje", DB2Type.VarChar);
            _myDB2Command.Parameters.Add("@Secuencia", DB2Type.Decimal);

            _myDB2Command.Parameters["@FechaEnvio"].Value = Convert.ToDecimal(string.Format("{0:yyyyMMdd}", DateTime.Now));
            _myDB2Command.Parameters["@HoraEnvio"].Value = Convert.ToDecimal(string.Format("{0:HHmmss}", DateTime.Now));
            _myDB2Command.Parameters["@Estado"].Value = _estado;
            _myDB2Command.Parameters["@Mensaje"].Value = MensajeError;
            _myDB2Command.Parameters["@Secuencia"].Value = Convert.ToDecimal(NumeroSecuencia);

            _myDB2Command.Transaction = _trans;
            _myDB2Command.ExecuteNonQuery();
            _trans.Commit();
        }

        /// <summary>
        /// Audita la transaccion en la tabla de auditoria
        /// </summary>
        /// <param name="NumeroSecuencia">Numero de secuencia de la transaccion</param>
        /// <param name="TipoMensaje">E: Enviado - R: Recibido</param>
        /// <param name="CampoDato">Detalle de la transaccion</param>
        public void AuditarTransaccion(string NumeroSecuencia, string TipoMensaje, string CampoDato)
        {
            DB2Transaction _trans = _myConn.BeginTransaction();
            DB2Command _myDB2Command = _myConn.CreateCommand();

            string _myQuery = "INSERT INTO CCVAUD VALUES(V7FECH, V7HORA, V7TMSJ, V7NSEQ, V7BUFF) ";
            _myQuery += "(?, ?, '?', ?, '?')";

            _myDB2Command.CommandText = _myQuery;

            _myDB2Command.Parameters.Add("@FechaEnvio", DB2Type.Decimal);
            _myDB2Command.Parameters.Add("@HoraEnvio", DB2Type.Decimal);
            _myDB2Command.Parameters.Add("@Tipo", DB2Type.VarChar);
            _myDB2Command.Parameters.Add("@Secuencia", DB2Type.Decimal);
            _myDB2Command.Parameters.Add("@Mensaje", DB2Type.VarChar);

            _myDB2Command.Parameters["@FechaEnvio"].Value = Convert.ToDecimal(string.Format("{0:yyyyMMdd}", DateTime.Now));
            _myDB2Command.Parameters["@HoraEnvio"].Value = Convert.ToDecimal(string.Format("{0:HHmmss}", DateTime.Now));
            _myDB2Command.Parameters["@Tipo"].Value = TipoMensaje;
            _myDB2Command.Parameters["@Secuencia"].Value = Convert.ToDecimal(NumeroSecuencia);
            _myDB2Command.Parameters["@Mensaje"].Value = CampoDato;

            _myDB2Command.Transaction = _trans;
            _myDB2Command.ExecuteNonQuery();
            _trans.Commit();
        }

        /// <summary>
        /// Actualiza operacion luego del alta recibida desde WS.
        /// </summary>
        /// <param name="NumeroSecuencia">Numero de secuencia de la transaccion</param>
        /// <param name="CodigoOperacion">Codigo de operacion recibido por CV</param>
        /// <param name="Plazo">Plazo actualizado</param>
        /// <param name="MensajeError">Para alta fallida: descripcion</param>
        /// <param name="Resultado">Resultado del proceso con CV, para saber que update realizar</param>
        public void ActualizarOperacion(string NumeroSecuencia, string CodigoOperacion, string Plazo, string MensajeError, bool Resultado)
        {
            DB2Transaction _trans = _myConn.BeginTransaction();
            DB2Command _myDB2Command = _myConn.CreateCommand();

            string _myQuery = "UPDATE CCVOPE ";
            
            if (Resultado)
                _myQuery += "SET V3IOPE = ?, V3PLAZ = ? ";
            else
                _myQuery += "SET V3MENS = '?' ";

            _myQuery += "WHERE  V3NSEQ = ?";

            _myDB2Command.CommandText = _myQuery;
            _myDB2Command.Parameters.Add("@Secuencia", DB2Type.Decimal);
            if (Resultado)
            {
                _myDB2Command.Parameters.Add("@Plazo", DB2Type.Decimal);
                _myDB2Command.Parameters.Add("@Operacion", DB2Type.Decimal);
            }
            else
            {
                _myDB2Command.Parameters.Add("@Mensaje", DB2Type.VarChar);
            }

            _myDB2Command.Parameters["@Secuencia"].Value = Convert.ToDecimal(NumeroSecuencia);

            if (Resultado)
            {
                _myDB2Command.Parameters["@Plazo"].Value = Convert.ToDecimal(Plazo);
                _myDB2Command.Parameters["@Operacion"].Value = Convert.ToDecimal(CodigoOperacion);
            }
            else
            {
                _myDB2Command.Parameters["@Mensaje"].Value = MensajeError;
            }

            _myDB2Command.Transaction = _trans;
            _myDB2Command.ExecuteNonQuery();
            _trans.Commit();

        }

        private DB2Connection _myConn;
        public class Transaccion
        {
            public string CodigoTransaccion { get; set; }
            public string NumeroSecuencia { get; set; }
            public string TramaMensaje { get; set; }
            public string EstadoMensaje { get; set; }
            public string FechaAlta { get; set; }
            public string HoraAlta { get; set; }

        }

    }
}
