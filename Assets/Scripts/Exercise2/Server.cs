using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Exercise2
{
    public class Server : MonoBehaviour
    {
        public string ServerName { get; set; } = "Server";

        [SerializeField] private TMPro.TextMeshProUGUI playersText;
        
        private Thread _acceptThread;
        private List<NetworkSocket> _clientSockets;
        private readonly List<Thread> _clientThreads = new();
        private readonly object _clientMutex = new();
        private bool _requestUpdateList = false;
        public void CreateServer()
        {
            NetworkData.EndPoint = new IPEndPoint(IPAddress.Any, NetworkData.Port);
            
            if(NetworkData.ProtocolType == ProtocolType.Tcp)
                InitServerTCP();
            else
                InitServerUDP();
            
            NetworkData.NetworkSocket.Socket.Bind(NetworkData.EndPoint);
            NetworkData.NetworkSocket.Socket.Listen(10);
            
            lock (_clientMutex)
            {
                _clientSockets = ((ServerNetworkSocket)NetworkData.NetworkSocket).ConnectedClients;
            }
            
            Debug.Log($"Server created with IP: {NetworkData.NetworkSocket.IPAddrStr} listening on port {NetworkData.Port}");
            
            // Accept incoming connections
            _acceptThread = new Thread(AcceptJob);
            _acceptThread.Start();
        }

        private void Update()
        {
            if (_requestUpdateList)
            {
                UpdatePlayerList();
                _requestUpdateList = false;
            }
        }

        public void StartGame()
        {
            SceneManager.LoadScene("Scenes/Exercise2/Lobby");
        }
        
        void InitServerUDP()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPAddress ipAddress = NetworkData.GetIPAddress();
            NetworkData.NetworkSocket = new ServerNetworkSocket(ServerName, serverSocket, ipAddress, ipAddress.ToString());
        }

        void InitServerTCP()
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPAddress ipAddress = NetworkData.GetIPAddress();
            NetworkData.NetworkSocket = new ServerNetworkSocket(ServerName, serverSocket, ipAddress, ipAddress.ToString());
        }

        void UpdatePlayerList()
        {
            playersText.text = "";
            foreach (var client in _clientSockets)
            {
                playersText.text += $"{client.Name}\n";
            }
        }
        
        void AcceptJob()
        {
            while (true)
            {
                AcceptConnections();
            }
        }

        void AcceptConnections()
        {
            // Accept clients
            Socket client = NetworkData.NetworkSocket.Socket.Accept();
            
            // Get client data
            string ipAddrStr = client.RemoteEndPoint.ToString();
            IPAddress addr = ((IPEndPoint)client.RemoteEndPoint).Address;
            int port = ((IPEndPoint)client.RemoteEndPoint).Port;
            
            Debug.Log($"Client connected from IP [{addr}] and port [{port}]");

            NetworkSocket clientSocket = new NetworkSocket("Client", client, addr, ipAddrStr);
            // Start the message handling
            Thread clientThread = new Thread(() => ReceiveJob(clientSocket));
            lock (_clientMutex)
            {
                _clientSockets.Add(clientSocket);
                _clientThreads.Add(clientThread);
            }
            clientThread.Start();
        }

        void ReceiveJob(NetworkSocket socket)
        {
            while (true)
            {
                int rBytes = NetworkData.ProtocolType == ProtocolType.Tcp ? ReceiveTCP(socket) : ReceiveUDP(socket);
                
                lock (_clientMutex)
                {
                    _requestUpdateList = true;
                }
                
                // Handle client disconnection
                if (rBytes == 0)
                {
                    lock (_clientMutex)
                    {
                        Debug.Log($"Disconnected client [{socket.Name}] from IP [{socket.IPAddrStr}]");
                        _clientSockets.Remove(socket);
                    }
                    break;
                }
            }
        }

        int ReceiveUDP(NetworkSocket socket)
        {
            byte[] data = new byte[2048];
            int rBytes = socket.Socket.ReceiveFrom(data, ref NetworkData.EndPoint);
            
            
            if (rBytes == 0)
                return rBytes;
            
            socket.Name = Encoding.ASCII.GetString(data, 0, rBytes);
            Debug.Log($"Client registered with name [{socket.Name}]");

            data = Encoding.ASCII.GetBytes(NetworkData.NetworkSocket.Name);
            socket.Socket.SendTo(data, data.Length, SocketFlags.None, NetworkData.EndPoint);
            
            return rBytes;
        }

        int ReceiveTCP(NetworkSocket socket)
        {
            byte[] data = new byte[2048];
            int rBytes = socket.Socket.Receive(data);
            
            if (rBytes == 0)
                return rBytes;
            
            socket.Name = Encoding.ASCII.GetString(data, 0, rBytes);
            Debug.Log($"Client registered with name [{socket.Name}]");

            data = Encoding.ASCII.GetBytes(NetworkData.NetworkSocket.Name);
            socket.Socket.Send(data, data.Length, SocketFlags.None);
            
            return rBytes;
        }

        private void OnDestroy()
        {
            if (_acceptThread is { IsAlive: true })
            {
                _acceptThread.Abort();
            }
            foreach (var thread in _clientThreads)
            {
                if (thread.IsAlive)
                {
                    thread.Abort();
                }
            }
        }
    }
}
