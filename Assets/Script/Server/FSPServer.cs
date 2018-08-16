using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SW;
using SW.Network.KCP;

public class FSPServer : Singleton<FSPServer>
{
    //帧间隔
    private long FRAME_TICK_INTERVAL = 666666;
    private bool m_bUseExternFrameTick = false;

    private FSPParam m_aParam = new FSPParam();
    private Thread m_aThreadMain;
    private bool m_bIsRunning = false;

    public bool IsRunning { get { return m_bIsRunning; } }

    private KCPSocket m_aGameSocket;

    private long m_nLogicLastTicks = 0;
    private long m_RealTicksAtStart = 0;

    private List<FSPSession> m_ltSession = new List<FSPSession>();
    private FSPRoom m_aRoom;
    public FSPRoom Room { get { return m_aRoom; } }

    private FSPGame m_aGame;
    public FSPGame Game { get { return m_aGame; } }

    public void SetFrameInterval(int serverFrameInterval, int clientFrameRateMultiple)
    {
        FRAME_TICK_INTERVAL = serverFrameInterval * 333333 * 30 / 1000;
        FRAME_TICK_INTERVAL = serverFrameInterval * 10000;
        m_aParam.serverFrameInterval = serverFrameInterval;
        m_aParam.clientFrameRateMultiple = clientFrameRateMultiple;
    }
    public void SetServerTimeout(int serverTimeout)
    {
        m_aParam.serverTimeout = serverTimeout;
    }

    public int GetFrameInterval()//MS
    {
        return (int)(FRAME_TICK_INTERVAL / 10000);
    }

    public bool UseExternFrameTick
    {
        get { return m_bUseExternFrameTick; }
        set { m_bUseExternFrameTick = value; }
    }

    #region 通讯参数

    public string GameIP
    {
        get { return m_aGameSocket != null ? m_aGameSocket.SelfIP : ""; }
    }

    public int GamePort
    {
        get { return m_aGameSocket != null ? m_aGameSocket.SelfPort : 0; }
    }


    //public string RoomIP
    //{
    //    get { return m_aRoomRPC != null ? m_RoomRPC.SelfIP : ""; }
    //}

    //public int RoomPort
    //{
    //    get { return m_RoomRPC != null ? m_RoomRPC.SelfPort : 0; }
    //}

    #endregion


    public FSPParam GetParam()
    {
        m_aParam.host = GameIP;
        m_aParam.port = GamePort;
        return m_aParam.Clone();
    }

    public int RealtimeSinceStartupMS
    {
        get
        {
            long dt = DateTime.Now.Ticks - m_RealTicksAtStart;
            return (int)(dt / 10000);
        }
    }

    private FSPSession GetSession(uint sid)
    {
        lock (m_ltSession)
        {
            for (int i = 0; i < m_ltSession.Count; i++)
            {
                if (m_ltSession[i].Id == sid)
                {
                    return m_ltSession[i];
                }
            }
        }
        return null;
    }

    internal FSPSession AddSession(uint sid)
    {
        FSPSession s = GetSession(sid);
        if (s != null)
        {
            return s;
        }

        s = new FSPSession(sid, m_aGameSocket);

        lock (m_ltSession)
        {
            m_ltSession.Add(s);
        }
        return s;
    }

    internal void DelSession(uint sid)
    {
        lock (m_ltSession)
        {
            for (int i = 0; i < m_ltSession.Count; i++)
            {
                if (m_ltSession[i].Id == sid)
                {
                    m_ltSession[i].Close();
                    m_ltSession.RemoveAt(i);
                    return;
                }
            }
        }
    }

    private void DelAllSession()
    {
        lock (m_ltSession)
        {
            for (int i = 0; i < m_ltSession.Count; i++)
            {
                m_ltSession[i].Close();
            }
            m_ltSession.Clear();
        }

    }

    protected override void InitSingleton()
    {
        base.InitSingleton();
    }

    public bool Start(int port)
    {
        if(m_bIsRunning)
        {
            return false;
        }

        DelAllSession();
        try
        {
            m_nLogicLastTicks = DateTime.Now.Ticks;
            m_RealTicksAtStart = m_nLogicLastTicks;

            m_aGameSocket = new KCPSocket(0, 1);
            m_aGameSocket.AddReceiveListener(OnReceive);
            m_bIsRunning = true;

            m_aRoom = new FSPRoom();
            m_aRoom.Create();

            m_aThreadMain = new Thread(Thread_Main) { IsBackground = true };
            m_aThreadMain.Start();
        }
        catch(Exception e)
        {
            Close();
            return false;
        }

        return true;
    }

    public void Close()
    {
        

        m_bIsRunning = false;

        if (m_aGame != null)
        {
            m_aGame.Dispose();
            m_aGame = null;
        }

        if (m_aRoom != null)
        {
            m_aRoom.Dispose();
            m_aRoom = null;
           
        }

        if (m_aGameSocket != null)
        {
            m_aGameSocket.Dispose();
            m_aGameSocket = null;
        }

        if (m_aThreadMain != null)
        {
            m_aThreadMain.Interrupt();
            m_aThreadMain = null;
        }

        DelAllSession();
    }

    #region 接收线程
    //------------------------------------------------------------

    private void OnReceive(byte[] buffer, int size, IPEndPoint remotePoint)
    {
        FSPDataC2S data = PBSerializer.NDeserialize<FSPDataC2S>(buffer);

        FSPSession session = GetSession(data.sid);
        if (session == null)
        {
            //没有这个玩家，不理它的数据
            return;
        }
        

        session.EndPoint = remotePoint;
        session.Receive(data);
    }

    #endregion

    #region 主循环线程
    private void Thread_Main()
    {
        while (m_bIsRunning)
        {
            try
            {
                DoMainLoop();
            }
            catch (Exception e)
            {
                
                Thread.Sleep(10);
            }
        }

        
    }


    //------------------------------------------------------------
    private void DoMainLoop()
    {
        long nowticks = DateTime.Now.Ticks;
        long interval = nowticks - m_nLogicLastTicks;

        if (interval > FRAME_TICK_INTERVAL)
        {
            m_nLogicLastTicks = nowticks - (nowticks % FRAME_TICK_INTERVAL);

            if (!m_bUseExternFrameTick)
            {
                EnterFrame();
            }
        }
    }

    public void EnterFrame()
    {
        if (m_bIsRunning)
        {
            m_aGameSocket.Update();
            //m_RoomRPC.RPCTick();

            if (m_aGame != null)
            {
                m_aGame.EnterFrame();
            }
        }
    }

    #endregion
    #region Game Logic

    public FSPGame StartGame()
    {
        if (m_aGame != null)
        {
            m_aGame.Dispose();
        }
        m_aGame = new FSPGame();
        m_aGame.Create(m_aParam);
        return m_aGame;
    }

    public void StopGame()
    {
        if (m_aGame != null)
        {
            m_aGame.Dispose();
            m_aGame = null;
        }
    }

    #endregion

}
