using SW;
using SW.Network;
using SW.Network.KCP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

public class FSPClient
{
    public delegate void FSPTimeoutListener(FSPClient target, int val);

    //线程模块
    private bool m_IsRunning = false;
    //基础通讯模块
    private KCPSocket m_Socket;
    private string m_Host;
    private int m_Port;
    private IPEndPoint m_HostEndPoint = null;
    private ushort m_SessionId = 0;

    //接收逻辑
    private Action<FSPFrame> m_RecvListener;
    private byte[] m_TempRecvBuf = new byte[10240];

    //发送逻辑
    private bool m_EnableFSPSend = true;
    private int m_AuthId;
    private FSPDataC2S m_TempSendData = new FSPDataC2S();
    private byte[] m_TempSendBuf = new byte[128];

    private bool m_WaitForReconnect = false;
    private bool m_WaitForSendAuth = false;

    #region 构造与析构
    public FSPClient()
    {

    }

    public void Close()
    {
        Disconnect();
        m_RecvListener = null;
        m_WaitForReconnect = false;
        m_WaitForSendAuth = false;
    }


    #endregion

    #region 设置通用参数

    public void SetSessionId(ushort sid)
    {
        m_SessionId = sid;
        m_TempSendData = new FSPDataC2S();
        m_TempSendData.vkeys.Add(new FSPVKey());
        m_TempSendData.sid = sid;
    }

    #endregion
    #region 设置FSP参数

    public void SetFSPAuthInfo(int authId)
    {
        m_AuthId = authId;
    }

    public void SetFSPListener(Action<FSPFrame> listener)
    {
        m_RecvListener = listener;
    }

    #endregion

    #region 基础连接函数
    public bool IsRunning { get { return m_IsRunning; } }
    public void VerifyAuth()
    {
        m_WaitForSendAuth = false;
        SendFSP(FSPVKeyBase.AUTH, m_AuthId, 0);
    }

    public void Reconnect()
    {
        m_WaitForReconnect = false;
        Disconnect();
        Connect(m_Host, m_Port);
        VerifyAuth();
    }

    public bool Connect(string host, int port)
    {
        if(m_Socket != null)
        {
            return false;
        }

        m_Host = host;
        m_Port = port;

        try
        {
            m_HostEndPoint = IPUtils.GetHostEndPoint(m_Host, m_Port);
            if(m_HostEndPoint == null)
            {
                Close();
                return false;
            }

            m_IsRunning = true;
            m_Socket = new KCPSocket(0, 1);
            m_Socket.AddReceiveListener(m_HostEndPoint, OnReceive);
        }
        catch(Exception e)
        {
            Close();
            return false;
        }
        return true;

    }
    private void Disconnect()
    {
        m_IsRunning = false;

        if (m_Socket != null)
        {
            m_Socket.Dispose();
            m_Socket = null;
        }


        m_HostEndPoint = null;
    }

    #endregion

    private void OnReceive(byte[] buffer, int size, IPEndPoint remotePoint)
    {
        FSPDataS2C data = PBSerializer.NDeserialize<FSPDataS2C>(buffer);
        if(m_RecvListener != null)
        {
            for (int i = 0; i < data.frames.Count; i++)
            {
                m_RecvListener(data.frames[i]);
            }
        }
    }

    public bool SendFSP(int vKey, int arg, int clientFrameId)
    {
        if(m_IsRunning)
        {
            FSPVKey cmd = m_TempSendData.vkeys[0];
            cmd.vkey = vKey;
            cmd.args = new int[] { arg };
            cmd.clientFrameId = (uint)clientFrameId;
            int len = PBSerializer.NSerialize(m_TempSendData, m_TempSendBuf);

            return m_Socket.SendTo(m_TempSendBuf, len, m_HostEndPoint);
        }

        return false;

    }

    public void EnterFrame()
    {
        if(!m_IsRunning)
        {
            return;
        }
        m_Socket.Update();

        if (m_WaitForReconnect)
        {
            Reconnect();
        }

        if (m_WaitForSendAuth)
        {
            VerifyAuth();
        }

    }
}
