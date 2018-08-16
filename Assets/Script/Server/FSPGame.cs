using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SW;

public enum FSPGameEndReason
{
    Normal = 0, //正常结束
    AllOtherExit = 1, //所有其他人都主动退出了
    AllOtherLost = 2,  //所有其他人都掉线了
}

public enum FSPGameState
{
    /// <summary>
    /// 0 初始状态
    /// </summary>
    None = 0,
    /// <summary>
    /// 游戏创建状态
    /// 只有在该状态下，才允许加入玩家
    /// 当所有玩家都发VKey.GameBegin后，进入下一个状态
    /// </summary>
    Create,
    /// <summary>
    /// 游戏开始状态
    /// 在该状态下，等待所有玩家发VKey.RoundBegin，或者 判断玩家是否掉线
    /// 当所有人都发送VKey.RoundBegin，进入下一个状态
    /// 当有玩家掉线，则从FSPGame中删除该玩家：
    ///     判断如果只剩下1个玩家了，则直接进入GameEnd状态，否则不影响游戏状态
    /// </summary>
    GameBegin,
    /// <summary>
    /// 回合开始状态
    /// （这个时候客户端可能在加载资源）
    /// 在该状态下，等待所有玩家发VKey.ControlStart， 或者 判断玩家是否掉线
    /// 当所有人都发送VKey.ControlStart，进入下一个状态
    /// 当有玩家掉线，则从FSPGame中删除该玩家：
    ///     判断如果只剩下1个玩家了，则直接进入GameEnd状态，否则不影响游戏状态
    /// </summary>
    RoundBegin,
    /// <summary>
    /// 可以开始操作状态
    /// （因为每个回合可能都会有加载过程，不同的玩家加载速度可能不同，需要用一个状态统一一下）
    /// 在该状态下，接收玩家的业务VKey， 或者 VKey.RoundEnd，或者VKey.GameExit
    /// 当所有人都发送VKey.RoundEnd，进入下一个状态
    /// 当有玩家掉线，或者发送VKey.GameExit，则从FSPGame中删除该玩家：
    ///     判断如果只剩下1个玩家了，则直接进入GameEnd状态，否则不影响游戏状态
    /// </summary>
    ControlStart,
    /// <summary>
    /// 回合结束状态
    /// （大部分游戏只有1个回合，也有些游戏有多个回合，由客户端逻辑决定）
    /// 在该状态下，等待玩家发送VKey.GameEnd，或者 VKey.RoundBegin（如果游戏不只1个回合的话）
    /// 当所有人都发送VKey.GameEnd，或者 VKey.RoundBegin时，进入下一个状态
    /// 当有玩家掉线，则从FSPGame中删除该玩家：
    ///     判断如果只剩下1个玩家了，则直接进入GameEnd状态，否则不影响游戏状态
    /// </summary>
    RoundEnd,
    /// <summary>
    /// 游戏结束状态
    /// 在该状态下，不再接收任何Vkey，然后给所有玩家发VKey.GameEnd，并且等待FSPServer关闭
    /// </summary>
    GameEnd,
}
public class FSPGame
{
    private FSPParam m_aFSPParam;

    private const int MaxPlayerNum = 31;

    private FSPGameState m_eState;
    private int m_nStateParam1;
    private int m_nStateParam2;

    public FSPGameState GameState { get { return m_eState; } }
    public int StateParam1 { get { return m_nStateParam1; } }
    public int StateParam2 { get { return m_nStateParam2; } }

    //Player的VKey标识
    private int m_GameBeginFlag = 0;
    private int m_RoundBeginFlag = 0;
    private int m_ControlStartFlag = 0;
    private int m_RoundEndFlag = 0;
    private int m_GameEndFlag = 0;

    //Round标志
    private int m_CurRoundId = 0;
    public int CurrentRoundId { get { return m_CurRoundId; } }

    //帧列表
    private int m_CurFrameId = 0;
    public int CurrentFrameId { get { return m_CurFrameId; } }

    //当前帧
    private FSPFrame m_LockedFrame = new FSPFrame();

    //玩家列表
    private List<FSPPlayer> m_ListPlayer = new List<FSPPlayer>();

    //等待删除的玩家
    private List<FSPPlayer> m_ListPlayersExitOnNextFrame = new List<FSPPlayer>();

    //有一个玩家退出游戏
    public Action<uint> onGameExit;

    //游戏真正结束
    public Action<int> onGameEnd;

    //=========================================================
    //延迟GC缓存
    public static bool UseDelayGC = false;
    private List<object> m_ListObjectsForDelayGC = new List<object>();

    public void Create(FSPParam param)
    {
        m_aFSPParam = param;
        m_CurRoundId = 0;

        ClearRound();
        SetGameState(FSPGameState.Create);

    }
    public void Dispose()
    {
        SetGameState(FSPGameState.None);
        for (int i = 0; i < m_ListPlayer.Count; i++)
        {
            FSPPlayer player = m_ListPlayer[i];
            FSPServer.Instance.DelSession(player.Sid);
            player.Dispose();
        }
        m_ListPlayer.Clear();
        m_ListObjectsForDelayGC.Clear();
        GC.Collect();
        onGameExit = null;
        onGameEnd = null;

    }

    public bool AddPlayer(uint playerId, uint sid)
    {
        if (m_eState != FSPGameState.Create)
        {
            return false;
        }

        FSPPlayer player = null;
        for (int i = 0; i < m_ListPlayer.Count; i++)
        {
            player = m_ListPlayer[i];
            if (player.Id == playerId)
            {
                m_ListPlayer.RemoveAt(i);
                FSPServer.Instance.DelSession(player.Sid);
                player.Dispose();
                break;
            }
        }

        if (m_ListPlayer.Count >= MaxPlayerNum)
        {
            
            return false;
        }

        FSPSession session = FSPServer.Instance.AddSession(sid);
        player = new FSPPlayer(playerId, m_aFSPParam.serverTimeout, session, OnPlayerReceive);
        m_ListPlayer.Add(player);

        return true;
    }

    private void OnPlayerReceive(FSPPlayer player, FSPVKey cmd)
    {
        if (UseDelayGC)
        {
            m_ListObjectsForDelayGC.Add(cmd);
        }

        HandleClientCmd(player, cmd);

    }
    /// <summary>
    /// 处理来自客户端的 Cmd
    /// 对其中的关键VKey进行处理
    /// 并且收集业务VKey
    /// </summary>
    /// <param name="player"></param>
    /// <param name="cmd"></param>
    protected virtual void HandleClientCmd(FSPPlayer player, FSPVKey cmd)
    {
        uint playerId = player.Id;
        if(!player.HasAuth)
        {
            if(cmd.vkey == FSPVKeyBase.AUTH)
            {
                player.SetAuth(cmd.args[0]);
            }
            return;
        }

        switch(cmd.vkey)
        {
            case FSPVKeyBase.GAME_BEGIN:
                {
                    SetFlag(playerId, ref m_GameBeginFlag, "m_GameBeginFlag");
                    break;
                }
            case FSPVKeyBase.ROUND_BEGIN:
                {
                    SetFlag(playerId, ref m_RoundBeginFlag, "m_RoundBeginFlag");
                    break;
                }
            case FSPVKeyBase.CONTROL_START:
                {
                    
                    SetFlag(playerId, ref m_ControlStartFlag, "m_ControlStartFlag");
                    break;
                }
            case FSPVKeyBase.ROUND_END:
                {
                    
                    SetFlag(playerId, ref m_RoundEndFlag, "m_RoundEndFlag");
                    break;
                }
            case FSPVKeyBase.GAME_END:
                {
                    
                    SetFlag(playerId, ref m_GameEndFlag, "m_GameEndFlag");
                    break;
                }
            case FSPVKeyBase.GAME_EXIT:
                {
                    
                    HandleGameExit(playerId, cmd);
                    break;
                }
            default:
                {
                    AddCmdToCurrentFrame(playerId, cmd);
                    break;
                }
        }
    }

    public void EnterFrame()
    {
        for(int i = 0; i < m_ListPlayersExitOnNextFrame.Count; i++)
        {
            FSPPlayer player = m_ListPlayersExitOnNextFrame[i];
            FSPServer.Instance.DelSession(player.Sid);
            player.Dispose();
        }
        m_ListPlayersExitOnNextFrame.Clear();

        
    }

    private void HandleGameState()
    {
        switch(m_eState)
        {
            case FSPGameState.None:
                {
                    //进入这个状态的游戏，马上将会被回收
                    //这里是否要考虑session中的所有消息都发完了？
                    break;
                }
            case FSPGameState.Create: //游戏刚创建，未有任何玩家加入, 这个阶段等待玩家加入
                {
                    OnState_Create();
                    break;
                }
            case FSPGameState.GameBegin: //游戏开始，等待RoundBegin
                {
                    OnState_GameBegin();
                    break;
                }
            case FSPGameState.RoundBegin: //回合已经开始，开始加载资源等，等待ControlStart
                {
                    OnState_RoundBegin();
                    break;
                }
            case FSPGameState.ControlStart: //在这个阶段可操作，这时候接受游戏中的各种行为包，并等待RoundEnd
                {
                    OnState_ControlStart();
                    break;
                }
            case FSPGameState.RoundEnd: //回合已经结束，判断是否进行下一轮，即等待RoundBegin，或者GameEnd
                {
                    OnState_RoundEnd();
                    break;
                }
            case FSPGameState.GameEnd://游戏结束
                {
                    OnState_GameEnd();
                    break;
                }
            default:
                break;
        }
    }
    #region 一系列状态处理函数
    /// <summary>
    /// 游戏创建状态
    /// 只有在该状态下，才允许加入玩家
    /// 当所有玩家都发VKey.GameBegin后，进入下一个状态
    /// </summary>
    protected virtual int OnState_Create()
    {
        //如果有任何一方已经鉴权完毕，则游戏进入GameBegin状态准备加载
        if (IsFlagFull(m_GameBeginFlag))
        {
            ResetRoundFlag();
            SetGameState(FSPGameState.GameBegin);
            AddCmdToCurrentFrame(FSPVKeyBase.GAME_BEGIN);
            return 0;
        }
        return 0;
    }
    /// <summary>
    /// 游戏开始状态
    /// 在该状态下，等待所有玩家发VKey.RoundBegin，或者 判断玩家是否掉线
    /// 当所有人都发送VKey.RoundBegin，进入下一个状态
    /// 当有玩家掉线，则从FSPGame中删除该玩家：
    ///     判断如果只剩下1个玩家了，则直接进入GameEnd状态，否则不影响游戏状态
    /// </summary>
    protected virtual int OnState_GameBegin()
    {
        if (CheckGameAbnormalEnd())
        {
            return 0;
        }

        if (IsFlagFull(m_RoundBeginFlag))
        {
            SetGameState(FSPGameState.RoundBegin);
            IncRoundId();
            AddCmdToCurrentFrame(FSPVKeyBase.ROUND_BEGIN, m_CurRoundId);

            return 0;
        }

        return 0;
    }

    /// <summary>
    /// 回合开始状态
    /// （这个时候客户端可能在加载资源）
    /// 在该状态下，等待所有玩家发VKey.ControlStart， 或者 判断玩家是否掉线
    /// 当所有人都发送VKey.ControlStart，进入下一个状态
    /// 当有玩家掉线，则从FSPGame中删除该玩家：
    ///     判断如果只剩下1个玩家了，则直接进入GameEnd状态，否则不影响游戏状态
    /// </summary>
    protected virtual int OnState_RoundBegin()
    {
        if (CheckGameAbnormalEnd())
        {
            return 0;
        }

        if (IsFlagFull(m_ControlStartFlag))
        {
            SetGameState(FSPGameState.ControlStart);
            AddCmdToCurrentFrame(FSPVKeyBase.CONTROL_START);
            return 0;
        }

        return 0;
    }

    /// <summary>
    /// 可以开始操作状态
    /// （因为每个回合可能都会有加载过程，不同的玩家加载速度可能不同，需要用一个状态统一一下）
    /// 在该状态下，接收玩家的业务VKey， 或者 VKey.RoundEnd，或者VKey.GameExit
    /// 当所有人都发送VKey.RoundEnd，进入下一个状态
    /// 当有玩家掉线，或者发送VKey.GameExit，则从FSPGame中删除该玩家：
    ///     判断如果只剩下1个玩家了，则直接进入GameEnd状态，否则不影响游戏状态
    /// </summary>
    protected virtual int OnState_ControlStart()
    {
        if (CheckGameAbnormalEnd())
        {
            return 0;
        }

        if (IsFlagFull(m_RoundEndFlag))
        {
            SetGameState(FSPGameState.RoundEnd);
            ClearRound();
            AddCmdToCurrentFrame(FSPVKeyBase.ROUND_END, m_CurRoundId);
            return 0;
        }

        return 0;
    }

    /// <summary>
    /// 回合结束状态
    /// （大部分游戏只有1个回合，也有些游戏有多个回合，由客户端逻辑决定）
    /// 在该状态下，等待玩家发送VKey.GameEnd，或者 VKey.RoundBegin（如果游戏不只1个回合的话）
    /// 当所有人都发送VKey.GameEnd，或者 VKey.RoundBegin时，进入下一个状态
    /// 当有玩家掉线，则从FSPGame中删除该玩家：
    ///     判断如果只剩下1个玩家了，则直接进入GameEnd状态，否则不影响游戏状态
    /// </summary>
    protected virtual int OnState_RoundEnd()
    {
        if (CheckGameAbnormalEnd())
        {
            return 0;
        }


        //这是正常GameEnd
        if (IsFlagFull(m_GameEndFlag))
        {
            SetGameState(FSPGameState.GameEnd, (int)FSPGameEndReason.Normal);
            AddCmdToCurrentFrame(FSPVKeyBase.GAME_END, (int)FSPGameEndReason.Normal);
            return 0;
        }


        if (IsFlagFull(m_RoundBeginFlag))
        {
            SetGameState(FSPGameState.RoundBegin);
            IncRoundId();
            AddCmdToCurrentFrame(FSPVKeyBase.ROUND_BEGIN, m_CurRoundId);
            return 0;
        }


        return 0;
    }
    protected virtual int OnState_GameEnd()
    {
        //到这里就等业务层去读取数据了 
        if (onGameEnd != null)
        {
            onGameEnd(m_nStateParam1);
            onGameEnd = null;
        }
        return 0;
    }

    #endregion

    private void IncRoundId()
    {
        ++m_CurRoundId;
    }

    private bool CheckGameAbnormalEnd()
    {
        //判断还剩下多少玩家，如果玩家少于2，则表示至少有玩家主动退出
        if (m_ListPlayer.Count < 2)
        {
            //直接进入GameEnd状态
            SetGameState(FSPGameState.GameEnd, (int)FSPGameEndReason.AllOtherExit);
            AddCmdToCurrentFrame(FSPVKeyBase.GAME_END, (int)FSPGameEndReason.AllOtherExit);
            return true;
        }
        // 检测玩家在线状态
        for (int i = 0; i < m_ListPlayer.Count; i++)
        {
            FSPPlayer player = m_ListPlayer[i];
            if (player.IsLose())
            {
                m_ListPlayer.RemoveAt(i);
                FSPServer.Instance.DelSession(player.Sid);
                player.Dispose();
                --i;
            }
        }

        //判断还剩下多少玩家，如果玩家少于2，则表示有玩家掉线了
        if (m_ListPlayer.Count < 2)
        {
            //直接进入GameEnd状态
            SetGameState(FSPGameState.GameEnd, (int)FSPGameEndReason.AllOtherLost);
            AddCmdToCurrentFrame(FSPVKeyBase.GAME_END, (int)FSPGameEndReason.AllOtherLost);
            return true;
        }

        return false;

    }
    public bool IsFlagFull(int flag)
    {
        if(m_ListPlayer.Count > 1)
        {
            for(int i = 0; i < m_ListPlayer.Count; i++)
            {
                int playerId = (int)m_ListPlayer[i].Id;
                if((flag & (0x01 << (playerId - 1))) == 0)
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }
    protected void AddCmdToCurrentFrame(int vKey, int arg = 0)
    {
        FSPVKey cmd = new FSPVKey();
        cmd.vkey = vKey;
        cmd.args = new int[] { arg };
        cmd.playerId = 0;
        AddCmdToCurrentFrame(0, cmd);
    }
    private void HandleGameExit(uint playerId, FSPVKey cmd)
    {
        AddCmdToCurrentFrame(playerId, cmd);
        FSPPlayer player = GetPlayer(playerId);

        if(player != null)
        {
            player.WaitForExit = true;
            if(onGameExit != null)
            {
                onGameExit(playerId);
            }
        }
    }

    private FSPPlayer GetPlayer(uint playerId)
    {
        FSPPlayer aPlayer = null;
        for(int i = 0; i < m_ListPlayer.Count; ++i)
        {
            aPlayer = m_ListPlayer[i];
            if (aPlayer.Id == playerId)
            {
                return aPlayer;
            }
        }

        return null;
    }
    protected void AddCmdToCurrentFrame(uint playerId, FSPVKey cmd)
    {
        cmd.playerId = playerId;
        m_LockedFrame.vkeys.Add(cmd);
    }
    private void SetFlag(uint playerId, ref int flag, string flagname)
    {
        flag |= (0x01 << ((int)playerId - 1));
    }

    private void ClsFlag(int playerId, ref int flag, string flagname)
    {
        flag &= (~(0x01 << (playerId - 1)));
    }
    protected void SetGameState(FSPGameState state, int param1 = 0, int param2 = 0)
    {
        m_eState = state;
        m_nStateParam1 = param1;
        m_nStateParam2 = param2;
    }

    private int ClearRound()
    {
        m_LockedFrame = new FSPFrame();
        m_CurFrameId = 0;

        ResetRoundFlag();

        for (int i = 0; i < m_ListPlayer.Count; i++)
        {
            if (m_ListPlayer[i] != null)
            {
                m_ListPlayer[i].ClearRound();
            }
        }

        return 0;
    }

    private void ResetRoundFlag()
    {
        m_RoundBeginFlag = 0;
        m_ControlStartFlag = 0;
        m_RoundEndFlag = 0;
        m_GameEndFlag = 0;
    }




}
