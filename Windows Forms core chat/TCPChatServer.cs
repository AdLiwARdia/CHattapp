using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

//https://github.com/AbleOpus/NetworkingSamples/blob/master/MultiServer/Program.cs
namespace Windows_Forms_Chat
{
    public class TCPChatServer : TCPChatBase
    {
        
        public Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        //connected clients
        public List<ClientSocket> clientSockets = new List<ClientSocket>();

        public List<string> moderators = new List<string>();


        public static TCPChatServer createInstance(int port, TextBox chatTextBox)
        {
            TCPChatServer tcp = null;
            //setup if port within range and valid chat box given
            if (port > 0 && port < 65535 && chatTextBox != null)
            {
                tcp = new TCPChatServer();
                tcp.port = port;
                tcp.chatTextBox = chatTextBox;

            }

            //return empty if user not enter useful details
            return tcp;
        }

        public void SetupServer()
        {
            chatTextBox.Text += "Setting up server...\n";
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            serverSocket.Listen(0);
 
            // Someone named garry automatically becomes a moderator and has the power to place power opun those who do not
            moderators.Add("Garry");

            //kick off thread to read connecting clients, when one connects, it'll call out AcceptCallback function
            serverSocket.BeginAccept(AcceptCallback, this);
            chatTextBox.Text += "Server setup complete\n";
        }



        public void CloseAllSockets()
        {
            foreach (ClientSocket clientSocket in clientSockets)
            {
                clientSocket.socket.Shutdown(SocketShutdown.Both);
                clientSocket.socket.Close();
            }
            clientSockets.Clear();
            serverSocket.Close();
        }

        public void AcceptCallback(IAsyncResult AR)
        {
            Socket joiningSocket;

            try
            {
                joiningSocket = serverSocket.EndAccept(AR);
            }
            catch (ObjectDisposedException) // I cannot seem to avoid this (on exit when properly closing sockets)
            {
                return;
            }

            ClientSocket newClientSocket = new ClientSocket();
            newClientSocket.socket = joiningSocket;

            clientSockets.Add(newClientSocket);
            //start a thread to listen out for this new joining socket. Therefore there is a thread open for each client
            joiningSocket.BeginReceive(newClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, newClientSocket);
            AddToChat("Client connected, waiting for request...");

            //we finished this accept thread, better kick off another so more people can join
            serverSocket.BeginAccept(AcceptCallback, null);
        }

        public void ReceiveCallback(IAsyncResult AR)
        {
            ClientSocket currentClientSocket = (ClientSocket)AR.AsyncState;
            
            int received;

            try
            {
                received = currentClientSocket.socket.EndReceive(AR);
            }
            catch (SocketException)
            {
                AddToChat("Client forcefully disconnected");
                // Don't shutdown because the socket may be disposed and its disconnected anyway.
                currentClientSocket.socket.Close();
                clientSockets.Remove(currentClientSocket);
                return;
            }

            byte[] recBuf = new byte[received];
            Array.Copy(currentClientSocket.buffer, recBuf, received);
            string text = Encoding.ASCII.GetString(recBuf);



            if (text.ToLower() == "!commands") // Client requested time
            {
                byte[] data = Encoding.ASCII.GetBytes("Commands are !commands !about !who !whisper !exit !username !changetag !time !mod !modlist !kick");
                currentClientSocket.socket.Send(data);
                AddToChat("Commands sent to client");
            }
            else if (text.ToLower() == "!exit") // Client wants to exit gracefully
            {
                // Always Shutdown before closing
                currentClientSocket.socket.Shutdown(SocketShutdown.Both);
                currentClientSocket.socket.Close();
                clientSockets.Remove(currentClientSocket);
                AddToChat("Client disconnected");
                return;
            }
            else if (text.ToLower().StartsWith("!username "))
            {
                string newUsername = text.Substring(10); // Get the new username from the command

                if (!string.IsNullOrWhiteSpace(newUsername))
                {
                    currentClientSocket.username = newUsername;
                    SendString("Username set to " + newUsername, currentClientSocket);
                }
                else
                {
                    SendString("Invalid username", currentClientSocket);
                }
            }
            else if (text.ToLower().StartsWith("!changetag "))
            {
                string newUsername = text.Substring(10);

                if (!string.IsNullOrWhiteSpace(newUsername))
                {
                    string oldUsername = currentClientSocket.username;
                    currentClientSocket.username = newUsername;

                    //tells the client who changed user names that it worked
                    SendString("Username changed to " + newUsername, currentClientSocket);

                    // Notify all clients about the username change
                    string message = $"{oldUsername} has changed their username to {newUsername}";
                    SendToAll(message, null);
                    AddToChat(message);
                }
                else
                {
                    SendString("Invalid username", currentClientSocket);
                }
            }
            else if (text.ToLower() == "!who")
            {
                StringBuilder names = new StringBuilder();
                foreach (ClientSocket client in clientSockets)
                {
                    if (client.username != null)
                    {
                        names.Append(client.username).Append(", ");
                    }
                }

                if (names.Length > 0)
                {
                    names.Length -= 2; // Remove the trailing comma and space
                    SendString("Connected users: " + names.ToString(), currentClientSocket);
                }
                else
                {
                    SendString("No users connected", currentClientSocket);
                }
            }
            else if (text.ToLower() == "!about")
            {
                // just prints out text on the command as about
                string aboutMessage = "This chat app was made by Edward and Edward and none other than the famous Edward in 2229 A00054425";
                SendString(aboutMessage, currentClientSocket);
            }
            else if (text.ToLower().StartsWith("!whisper "))
            {
                string[] parts = text.Split(' ');
                if (parts.Length >= 3)
                {
                    //sets up for hunting the target
                    string targetUsername = parts[1];
                    string message = text.Substring(10 + targetUsername.Length);

                    //seeks the client like a prey
                    ClientSocket targetClient = clientSockets.Find(client => client.username == targetUsername);
                    if (targetClient != null)
                    {
                        //just a way to find out who whispered to you
                        string whisperMessage = $"{currentClientSocket.username} whispered: {message}";
                        SendString(whisperMessage, targetClient);
                        SendString("Whisper sent to " + targetUsername, currentClientSocket);
                    }
                    else
                    {
                        // mostly there to say how bad you are at spelling names
                        SendString("User not found: " + targetUsername, currentClientSocket);
                    }
                }
                else
                {
                    SendString("Invalid whisper format", currentClientSocket);
                }
            }
            else if (text.ToLower() == "!time")
            {
                //telss time
                string currentTime = DateTime.Now.ToString("hh:mm:ss tt");
                SendString("Current time: " + currentTime, currentClientSocket);
            }
            // give the power of a thousand suns on one client
            else if (text.ToLower().StartsWith("!mod "))
            {
                // whoever gets named gets modded or get removed from their throne
                if (moderators.Contains(currentClientSocket.username))
                {
                    string targetUsername = text.Substring(5); 

                    if (!string.IsNullOrWhiteSpace(targetUsername))
                    {
                        if (moderators.Contains(targetUsername))
                        {
                            moderators.Remove(targetUsername);
                            SendString(targetUsername + " demoted from moderator", currentClientSocket);
                        }
                        else
                        {
                            moderators.Add(targetUsername);
                            SendString(targetUsername + " promoted to moderator", currentClientSocket);
                        }
                    }
                    else
                    {
                        SendString("Invalid username", currentClientSocket);
                    }
                }
                else
                {
                    // tells you if you are not a mod
                    SendString("You are not a moderator", currentClientSocket);
                }
            }
            else if (text.ToLower().StartsWith("!kick "))
            {
                // problem where kick needs to be !kick[name] instead of !kick space [name]
                //decided not to fix it
                // seeks the name of the target
                if (moderators.Contains(currentClientSocket.username))
                {
                    string targetUsername = text.Substring(5); 

                    ClientSocket targetClient = clientSockets.Find(client => client.username == targetUsername);
                    // if found their client is shutdown
                    if (targetClient != null)
                    {
                        targetClient.socket.Shutdown(SocketShutdown.Both);
                        targetClient.socket.Close();
                        clientSockets.Remove(targetClient);
                        SendToAll(targetUsername + " has been kicked by " + currentClientSocket.username, null);
                        AddToChat(targetUsername + " has been kicked by " + currentClientSocket.username);
                    }
                    else
                    {
                        SendString("User not found: " + targetUsername, currentClientSocket);
                    }
                }
                else
                {
                    SendString("You are not a moderator", currentClientSocket);
                }
            }
            // lists all those who are moderators
            else if (text.ToLower() == "!modlist")
            {
                if (moderators.Count > 0)
                {
                    SendString("Moderators: " + string.Join(", ", moderators), currentClientSocket);
                }
                else
                {
                    SendString("No moderators", currentClientSocket);
                }
            }
            else
            {
                if (currentClientSocket.username == null)
                {
                    SendString("you have not settted a namer", currentClientSocket);
                }
                else
                {

                    //normal message broadcast out to all clients
                    string message = $"{currentClientSocket.username}: " + text;
                    SendToAll(message, currentClientSocket);
                    AddToChat(message);

                }

            }
            //we just received a message from this socket, better keep an ear out with another thread for the next one
            currentClientSocket.socket.BeginReceive(currentClientSocket.buffer, 0, ClientSocket.BUFFER_SIZE, SocketFlags.None, ReceiveCallback, currentClientSocket);
        }

        public void SendToAll(string str, ClientSocket from)
        {
            foreach(ClientSocket c in clientSockets)
            {
                if(from == null || !from.socket.Equals(c))
                {
                    byte[] data = Encoding.ASCII.GetBytes(str);
                    c.socket.Send(data);
                }
            }
        }

        public void SendString(string text, ClientSocket to)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(text);
            to.socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
        }

        
    }
}
