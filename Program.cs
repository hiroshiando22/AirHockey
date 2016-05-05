using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Collections.Concurrent;

namespace AirHockyServer
{
    class Program
    {
        public static double delt = 1;
        public static double RestitutionCoefficient_Puck_Wall = 1;
        public static double RestitutionCoefficient_Puck_Mallet = 1;
        public static double PuckMaxSpeed = 10;
        public static int puckRadius = 25;
        public static int malletRadius = 25;
        public static double friction = 0.995;

        public static class Court
        {
            public static int width = 450;
            public static int height = 600;
            public static int GoalWidth = 200;
        }

        [DataContract]
        public class Puck
        {
            [DataMember]
            public int R;

            [DataMember]
            public Matrix2.Vector X;

            public Matrix2.Vector V;

            public Puck()
            {
                R = puckRadius;
                X = new Matrix2.Vector(Court.width / 2, Court.height / 2);
                V = new Matrix2.Vector(0, 0);
            }
        }
        static Puck puck = new Puck();

        [DataContract]
        public class Mallet
        {
            [DataMember]
            public int R;

            [DataMember]
            public int N;

            [DataMember]
            public Matrix2.Vector X;

            public Matrix2.Vector V;

            public string action;

            public Mallet()
            {
                R = malletRadius;
                X = new Matrix2.Vector(R, Court.height - R);
                V = new Matrix2.Vector(0, 0);
            }
        }
        static List<Mallet> _mallets = new List<Mallet>();
        static ConcurrentQueue<Mallet> _malletsTemp = new ConcurrentQueue<Mallet>();

        [DataContract]
        public class Message
        {
            [DataMember]
            public string message;

            [DataMember]
            public Puck puck;

            [DataMember]
            public List<Mallet> mallets;

            public Message(string message)
            {
                this.message = message;
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Default Server IP:");
            Console.WriteLine("192.168.0.46:8080");
            Console.Write("\n");
            StartServer("192.168.0.46:8080");
            Console.WriteLine("{0}:Air Hockey Server Start", DateTime.Now.ToString());

            while (true)
            {
                DateTime startDt = DateTime.Now;
                Reload();
                DateTime endDt = DateTime.Now;
                TimeSpan ts = endDt - startDt;
                Thread.Sleep(Math.Max(1000 / 60 - (int)ts.TotalMilliseconds, 0));
            }

            /*
            Console.ReadKey();
            Parallel.ForEach(_client, p =>
            {
                if (p.State == WebSocketState.Open) p.CloseAsync(WebSocketCloseStatus.NormalClosure, "", System.Threading.CancellationToken.None);
            });
            */
        }

        static List<WebSocket> _client = new List<WebSocket>();
        static async void StartServer(string IP)
        {
            var httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://" + IP + "/");
            httpListener.Start();

            while (true)
            {
                var listenerContext = await httpListener.GetContextAsync();
                if (listenerContext.Request.IsWebSocketRequest)
                {
                    ProcessRequest(listenerContext);
                }
                else
                {
                    listenerContext.Response.StatusCode = 400;
                    listenerContext.Response.Close();
                }
            }
        }

        static async void ProcessRequest(HttpListenerContext listenerContext)
        {
            Console.WriteLine("{0}:New Session:{1}", DateTime.Now.ToString(), listenerContext.Request.RemoteEndPoint.Address.ToString());
            var ws = (await listenerContext.AcceptWebSocketAsync(subProtocol: null)).WebSocket;
            _client.Add(ws);

            var malletAdd = new Mallet();
            malletAdd.action = "Add";
            _malletsTemp.Enqueue(malletAdd);
            malletAdd = null;

            while (ws.State == WebSocketState.Open)
            {
                try
                {
                    var buff = new ArraySegment<byte>(new byte[1024]);
                    var ret = await ws.ReceiveAsync(buff, System.Threading.CancellationToken.None);
                    if (ret.MessageType == WebSocketMessageType.Text)
                    {
                        string str = Encoding.UTF8.GetString(buff.Take(ret.Count).ToArray());
                        string[] strspl = str.Split(',');
                        if (strspl[0] == "data")
                        {
                            var malletMove = new Mallet();
                            malletMove.X = new Matrix2.Vector(int.Parse(strspl[1]), int.Parse(strspl[2]));
                            malletMove.N = _client.IndexOf(ws);
                            malletMove.action = "Move";
                            _malletsTemp.Enqueue(malletMove);
                            malletMove = null;
                        }
                        str = null;
                        strspl = null;
                    }
                    else if (ret.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("{0}:Session Close:{1}", DateTime.Now.ToString(), listenerContext.Request.RemoteEndPoint.Address.ToString());
                        var malletRemove = new Mallet();
                        malletRemove.N = _client.IndexOf(ws);
                        malletRemove.action = "Remove";
                        _malletsTemp.Enqueue(malletRemove);
                        malletRemove = null;
                        break;
                    }
                    ret = null;
                }
                catch
                {
                    Console.WriteLine("{0}:Session Abort:{1}", DateTime.Now.ToString(), listenerContext.Request.RemoteEndPoint.Address.ToString());
                    var malletRemove = new Mallet();
                    malletRemove.N = _client.IndexOf(ws);
                    malletRemove.action = "Remove";
                    _malletsTemp.Enqueue(malletRemove);
                    malletRemove = null;
                    break;
                }
            }
            _client.Remove(ws);
            ws.Dispose();
        }

        public static void Reload()
        {
            // --- mallets
            for (int i = 0; i < _mallets.Count; i++)
            {
                _mallets[i].V = new Matrix2.Vector(0, 0);
            }
            while (0 < _malletsTemp.Count)
            {
                Mallet m;
                _malletsTemp.TryDequeue(out m);
                if (m.action == "Add")
                {
                    _mallets.Add(m);
                }
                else if (m.action == "Move")
                {
                    _mallets[m.N].V = Matrix2.VectorSum(m.X, Matrix2.VectorScalarProduct(-1, _mallets[m.N].X));
                    _mallets[m.N].X = m.X;
                    if (_mallets[m.N].X.x <= 0 + _mallets[m.N].R)
                    {
                        _mallets[m.N].X.x = 0 + _mallets[m.N].R;
                    }
                    else if (_mallets[m.N].X.x >= Court.width - _mallets[m.N].R)
                    {
                        _mallets[m.N].X.x = Court.width - _mallets[m.N].R;
                    }
                    if (_mallets[m.N].X.y <= 0 + _mallets[m.N].R)
                    {
                        _mallets[m.N].X.y = 0 + _mallets[m.N].R;
                    }
                    else if (_mallets[m.N].X.y >= Court.height - _mallets[m.N].R)
                    {
                        _mallets[m.N].X.y = Court.height - _mallets[m.N].R;
                    }
                }
                else if (m.action == "Remove")
                {
                    _mallets.RemoveAt(m.N);
                }
                m = null;
            }

            // --- puck
            puck.X = Matrix2.VectorSum(puck.X, Matrix2.VectorScalarProduct(delt, puck.V));
            for (int i = 0; i < _mallets.Count; i++)
            {
                if (Math.Sqrt(Matrix2.VectorInnerproduct(Matrix2.VectorSum(_mallets[i].X, Matrix2.VectorScalarProduct(-1, puck.X)),
                                                        Matrix2.VectorSum(_mallets[i].X, Matrix2.VectorScalarProduct(-1, puck.X))))
                                                < _mallets[i].R + puck.R)
                {
                    double arg = Math.Atan2(_mallets[i].X.y - puck.X.y, _mallets[i].X.x - puck.X.x) + Math.PI / 2.0;
                    puck.X = Matrix2.VectorSum(_mallets[i].X,
                                               new Matrix2.Vector((_mallets[i].R + puck.R) * Math.Cos(arg + Math.PI / 2.0),
                                                                  (_mallets[i].R + puck.R) * Math.Sin(arg + Math.PI / 2.0)));
                    puck.V = Matrix2.VectorSum(
                                Matrix2.MatrixVectorProduct(
                                    Matrix2.MatrixMatrixProduct(
                                        Matrix2.MatrixMatrixProduct(new Matrix2.Matrix(Math.Cos(arg), -Math.Sin(arg), Math.Sin(arg), Math.Cos(arg)),
                                                                    new Matrix2.Matrix(1, 0, 0, -RestitutionCoefficient_Puck_Mallet)),
                                        new Matrix2.Matrix(Math.Cos(arg), Math.Sin(arg), -Math.Sin(arg), Math.Cos(arg))),
                                    Matrix2.VectorSum(puck.V, Matrix2.VectorScalarProduct(-1, _mallets[i].V))),
                                _mallets[i].V);
                }
            }

            if (puck.X.x <= 0 + puck.R)
            {
                puck.X.x = 0 + puck.R;
                puck.V.x = -RestitutionCoefficient_Puck_Wall * puck.V.x;
            }
            else if (puck.X.x >= Court.width - puck.R)
            {
                puck.X.x = Court.width - puck.R;
                puck.V.x = -RestitutionCoefficient_Puck_Wall * puck.V.x;
            }

            if (puck.X.y <= 0 + puck.R
                && ((puck.X.x < (Court.width - Court.GoalWidth) / 2) || (puck.X.x > (Court.width + Court.GoalWidth) / 2)))
            {
                puck.X.y = 0 + puck.R;
                puck.V.y = -RestitutionCoefficient_Puck_Wall * puck.V.y;
            }
            else if (puck.X.y <= 0 - puck.R
                && ((puck.X.x > (Court.width - Court.GoalWidth) / 2) && (puck.X.x < (Court.width + Court.GoalWidth) / 2)))
            {
                puck.X = new Matrix2.Vector(Court.width / 2, Court.height / 2);
                puck.V = new Matrix2.Vector(0, 0);

                // --- message
                Message m = new Message("Goal A");
                string s;
                using (MemoryStream stream = new MemoryStream())
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Message));
                    ser.WriteObject(stream, m);
                    stream.Position = 0;
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        s = reader.ReadToEnd();
                    }
                    ser = null;
                }
                Parallel.ForEach(_client,
                                p => p.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(s)),
                                WebSocketMessageType.Text,
                                true,
                                System.Threading.CancellationToken.None));
                s = null;
                m = null;
            }

            if (puck.X.y >= Court.height - puck.R
                && ((puck.X.x < (Court.width - Court.GoalWidth) / 2) || (puck.X.x > (Court.width + Court.GoalWidth) / 2)))
            {
                puck.X.y = Court.height - puck.R;
                puck.V.y = -RestitutionCoefficient_Puck_Wall * puck.V.y;
            }
            else if (puck.X.y >= Court.height + puck.R
                && ((puck.X.x > (Court.width - Court.GoalWidth) / 2) && (puck.X.x < (Court.width + Court.GoalWidth) / 2)))
            {
                puck.X = new Matrix2.Vector(Court.width / 2, Court.height / 2);
                puck.V = new Matrix2.Vector(0, 0);

                // --- message
                Message m = new Message("Goal B");
                string s;
                using (MemoryStream stream = new MemoryStream())
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Message));
                    ser.WriteObject(stream, m);
                    stream.Position = 0;
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        s = reader.ReadToEnd();
                    }
                    ser = null;
                }
                Parallel.ForEach(_client,
                                p => p.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(s)),
                                WebSocketMessageType.Text,
                                true,
                                System.Threading.CancellationToken.None));
                s = null;
                m = null;
            }

            if (PuckMaxSpeed < Math.Sqrt(Matrix2.VectorInnerproduct(puck.V, puck.V)))
            {
                puck.V = Matrix2.VectorScalarProduct(PuckMaxSpeed / Math.Sqrt(Matrix2.VectorInnerproduct(puck.V, puck.V)), puck.V);
            }
            puck.V = Matrix2.VectorScalarProduct(friction, puck.V);

            // --- message
            Message message = new Message("position");
            message.puck = puck;
            message.mallets = _mallets;
            string str;
            using (MemoryStream stream = new MemoryStream())
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Message));
                ser.WriteObject(stream, message);
                stream.Position = 0;
                using (StreamReader reader = new StreamReader(stream))
                {
                    str = reader.ReadToEnd();
                }
                ser = null;
            }
            Parallel.ForEach(_client,
                            p => p.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(str)),
                            WebSocketMessageType.Text,
                            true,
                            System.Threading.CancellationToken.None));
            str = null;
            message = null;
        }
    }

    [DataContract]
    public static class Matrix2
    {
        [DataContract]
        public class Vector
        {
            [DataMember]
            public double x;

            [DataMember]
            public double y;

            public Vector(double x, double y)
            {
                this.x = x;
                this.y = y;
            }
        }

        [DataContract]
        public class Matrix
        {
            [DataMember]
            public double _11;

            [DataMember]
            public double _12;

            [DataMember]
            public double _21;

            [DataMember]
            public double _22;

            public Matrix(double _11, double _12, double _21, double _22)
            {
                this._11 = _11;
                this._12 = _12;
                this._21 = _21;
                this._22 = _22;
            }
        }

        public static Vector VectorScalarProduct(double a, Vector b)
        {
            return new Vector(a * b.x,
                                a * b.y);
        }

        public static Vector VectorSum(Vector a, Vector b)
        {
            return new Vector(a.x + b.x,
                                a.y + b.y);
        }

        public static double VectorInnerproduct(Vector a, Vector b)
        {
            return a.x * b.x + a.y * b.y;
        }

        public static Matrix MatrixMatrixProduct(Matrix a, Matrix b)
        {
            return new Matrix(a._11 * b._11 + a._12 * b._21,
                                a._11 * b._12 + a._12 * b._22,
                                a._21 * b._11 + a._22 * b._21,
                                a._21 * b._12 + a._22 * b._22);
        }

        public static Vector MatrixVectorProduct(Matrix a, Vector b)
        {
            return new Vector(a._11 * b.x + a._12 * b.y,
                                a._21 * b.x + a._22 * b.y);
        }
    }
}
