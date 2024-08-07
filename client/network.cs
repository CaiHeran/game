﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ConstrainedExecution;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Threading;

namespace Client
{
    internal static class Server
    {
        private static SslStream? stream;
        public static bool IsConnected {
            get {
                return stream != null;
            }
        }
        private static AsyncQueue<string> messages = new();                           // 消息队列
        public static bool Connect(string host, int port)                             // 连接服务器，参数为服务器和端口
        {
            TcpClient client = new TcpClient(host, port);
            SslStream sslStream = new SslStream(
                client.GetStream(),
                false,
                new RemoteCertificateValidationCallback(ValidateServerCertificate),
                null
            );
            try
            {
                sslStream.AuthenticateAsClient("");                                   // ？
            }
            catch (Exception e)                                                       // 认证失败
            {
                client.Close();                                                       // 关闭连接
                ExceptionDispatchInfo.Capture(e).Throw();
            }

            if (!sslStream.IsAuthenticated)
            {
                client.Close();
                return false;
            }

            stream = sslStream;
            Process.Start();
            Task.Factory.StartNew(Reader, TaskCreationOptions.LongRunning);           // 开启消息接受和处理
            Task.Factory.StartNew(Writer, TaskCreationOptions.LongRunning);           // 开启消息输出
            return true;
        }

        public static void Send(string message)                                       // 发送消息（下层实践）
        {
            messages.Enqueue(message);                                                // 将发送消息的事件加入队列
        }
        public static void Send(int type, string data)                                // 发送消息（上层整合）
        {
            Send($$"""{"type":{{type}},"data":{{data}}}""");
        }
        private static async Task Writer()
        {
            try {
                StreamWriter writer = new(stream);
                while (true)
                {
                    string msg = await messages.DequeueAsync();                           // 等待并获取刚处理的信息
                    await writer.WriteLineAsync(msg);                                     // 输出信息并等待输出完成
                    await writer.FlushAsync();                                            // 刷新缓冲区
                }
            }
            catch (Exception e)
            {
                stream.Close();
            }
        }
        private static async Task Reader()
        {
            try
            {
                StreamReader sr = new(stream, Encoding.UTF8);
                while (true)
                {
                    StringBuilder messageData = new StringBuilder();                      // 读取信息队列
                    string? msg = await sr.ReadLineAsync();                               // 等待信息读取完毕
                    if (msg == null) { break; }                                           // ???（蔡和然的疑问）（吴桐的猜测：用return？）
                    Task.Run(() => Process.ProcessMessage(msg));                          // 处理信息
                }
            }
            catch (Exception e)
            {
                stream.Close();
            }
        }
        private static bool ValidateServerCertificate(                                // 验证服务器证书
                object sender,
                X509Certificate certificate,
                X509Chain chain,
                SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            string information = $"Server's certificate:\n{certificate}\nAuthenticate?";

            var res = MessageBox.Show($"{information}",
              "确认",
              MessageBoxButtons.YesNo,
              MessageBoxIcon.Information);

            return res == DialogResult.Yes;                                             // 验证用户回答
        }
    }
}
