using SW;
using SW.Network.KCP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

public class FSPSession
{
    public uint m_nSid;
    public uint Id { get { return m_nSid; } }

    private Action<FSPDataC2S> m_RecvListener;
    private KCPSocket m_aSocket;

    private byte[] m_SendBuffer = new byte[40960];
    private bool m_bIsEndPointChanged = false;

    private IPEndPoint m_aEndPoint;
    public IPEndPoint EndPoint
    {
        get { return m_aEndPoint; }
        set
        {
            if (m_aEndPoint == null || !m_aEndPoint.Equals(value))
            {
                m_bIsEndPointChanged = true;
            }
            else
            {
                m_bIsEndPointChanged = false;
            }

            m_aEndPoint = value;
        }
    }

    public bool IsEndPointChanged { get { return m_bIsEndPointChanged; } }

    public FSPSession(uint sid, KCPSocket socket)
    {
        m_aSocket = socket;
        m_nSid = sid;

    }

    public virtual void Close()
    {
        if(m_aSocket != null)
        {
            m_aSocket.CloseKcp(EndPoint);
            m_aSocket = null;
        }
    }

    public void SetReceiveListener(Action<FSPDataC2S> listener)
    {
        m_RecvListener = listener;
    }

    public bool Send(FSPFrame frame)
    {
        if(null != m_aSocket)
        {
            FSPDataS2C data = new FSPDataS2C();
            data.frames.Add(frame);
            int len = PBSerializer.NSerialize(data, m_SendBuffer);
            return m_aSocket.SendTo(m_SendBuffer, len, EndPoint);
        }
        return false;
    }

    public void Receive(FSPDataC2S data)
    {
        if(null != m_RecvListener)
        {
            m_RecvListener(data);
        }
    }
}
