using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace RFReaderConsole
{
    public class RFGateSocket
    {
        #region Variables
        Socket _Socket;
        IPEndPoint _IPEndPoint;

        // Socket işlemleri sırasında oluşabilecek errorları bu enum ile handle edebiliriz.
        SocketError socketError;
        byte[] tempBuffer = new byte[1024];
        #endregion

        #region Constructor
        public RFGateSocket(IPEndPoint ipEndPoint)
        {
            _IPEndPoint = ipEndPoint;

            // Socket'i tanımlıyoruz IPv4, socket tipimiz stream olacak ve TCP Protokolü ile haberleşeceğiz. 
            // TCP Protokolünde server belirlenen portu dinler ve gelen istekleri karşılar oysaki UDP Protokolünde tek bir socket üzerinden birden çok client'a ulaşmak mümkündür.
            _Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }
        #endregion

        #region Public Methods
        public void Start()
        {
            // BeginConnect ile asenkron olarak bir bağlantı başlatıyoruz.
            _Socket.BeginConnect(_IPEndPoint, OnBeginConnect, null);
        }

        public void SendData(string message)
        {
            using (var ms = new MemoryStream())
            {
                // İlgili object'imizi binary'e serialize ediyoruz.
                new BinaryFormatter().Serialize(ms, message);
                IList<ArraySegment<byte>> data = new List<ArraySegment<byte>>();

                data.Add(new ArraySegment<byte>(ms.ToArray()));

                // Gönderme işlemine başlıyoruz.
                _Socket.BeginSend(data, SocketFlags.None, out socketError, (asyncResult) =>
                {
                    // Gönderme işlemini bitiriyoruz.
                    int length = _Socket.EndSend(asyncResult, out socketError);

                    if (length <= 0 || socketError != SocketError.Success)
                    {
                        Console.WriteLine("Server bağlantısı koptu!");
                        return;
                    }
                }, null);

                if (socketError != SocketError.Success)
                    Console.WriteLine("Server bağlantısı koptu!");
            }
        }
        #endregion

        #region Private Methods
        void OnBeginConnect(IAsyncResult asyncResult)
        {
            try
            {
                // Bağlanma işlemini bitiriyoruz.
                _Socket.EndConnect(asyncResult);

                // Bağlandığımız socket üzerinden datayı dinlemeye başlıyoruz.
                _Socket.BeginReceive(tempBuffer, 0, tempBuffer.Length, SocketFlags.None, OnBeginReceive, null);
            }
            catch (SocketException)
            {
                // Servera bağlanamama durumlarında bize SocketException fırlatıcaktır. Hataları burada handle edebilirsiniz.
                Console.WriteLine("Servera bağlanılamıyor!");
            }
        }

        void OnBeginReceive(IAsyncResult asyncResult)
        {
            // Almayı bitiriyoruz ve geriye gelen byte array'in boyutunu vermektedir.
            int receivedDataLength = _Socket.EndReceive(asyncResult, out socketError);

            if (receivedDataLength <= 0 || socketError != SocketError.Success)
            {
                // Gelen byte array verisi boş ise bağlantı kopmuş demektir. Burayı istediğiniz gibi handle edebilirsiniz.
                Console.WriteLine("Server bağlantısı koptu!");
                return;
            }

            // Tekrardan socket üzerinden datayı dinlemeye başlıyoruz.
            _Socket.BeginReceive(tempBuffer, 0, tempBuffer.Length, SocketFlags.None, OnBeginReceive, null);
        }
        #endregion
    }
}
