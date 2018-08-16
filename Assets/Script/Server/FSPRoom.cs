using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

[ProtoContract]
public class FSPPlayerData
{
    [ProtoMember(1)]
    public uint id;
    [ProtoMember(2)]
    public string name;
    [ProtoMember(3)]
    public uint userId;
    [ProtoMember(4)]
    public uint sid;
    [ProtoMember(5)]
    public bool isReady;
    [ProtoMember(6)]
    public byte[] customPlayerData;


    public override string ToString()
    {
        return string.Format("[FSPPlayerData] id:{0}, name:{1}, userId:{2}, sid:{3}, isReady:{4}", id, name, userId, sid, isReady);
    }
}

[ProtoContract]
public class FSPRoomData
{
    [ProtoMember(1)]
    public uint id;
    [ProtoMember(2)]
    public List<FSPPlayerData> players = new List<FSPPlayerData>();
}

public class DictionaryEx<TKey, TValue> : Dictionary<TKey, TValue>
{
    public new TValue this[TKey indexKey]
    {
        set { base[indexKey] = value; }
        get
        {
            try
            {
                return base[indexKey];
            }
            catch (Exception)
            {
                return default(TValue);
            }
        }
    }
}

public class FSPRoom
{
    private FSPRoomData m_data = new FSPRoomData();
    private byte[] m_customGameParam;
    private DictionaryEx<uint, IPEndPoint> m_mapUserId2Address = new DictionaryEx<uint, IPEndPoint>();

    public FSPRoom()
    {

    }

    public uint Id { get { return m_data.id; } }

    public void Create()
    {
        m_data.id = 1;
    }

    public void Dispose()
    {

    }

    public void SetCustomGameParam(byte[] custom)
    {
        m_customGameParam = custom;
    }

    private void AddPlayer(uint userId, string name, byte[] customPlayerData, IPEndPoint address)
    {
        FSPPlayerData data = GetPlayerInfoByUserId(userId);
        if (data == null)
        {
            data = new FSPPlayerData();
            m_data.players.Add(data);
            data.id = (uint)m_data.players.Count;
            data.sid = data.id;
        }
        data.customPlayerData = customPlayerData;
        data.isReady = false;
        data.userId = userId;
        data.name = name;
        m_mapUserId2Address[userId] = address;
    }

    private void RemovePlayerByUserId(uint userId)
    {
        int i = GetPlayerIndexByUserId(userId);
        if (i >= 0)
        {
            m_data.players.RemoveAt(i);
        }

        if (m_mapUserId2Address.ContainsKey(userId))
        {
            m_mapUserId2Address.Remove(userId);
        }
    }
    
    private void RemovePlayerById(uint playerId)
    {
        for (int i = 0; i < m_data.players.Count; i++)
        {
            if (m_data.players[i].id == playerId)
            {
                m_data.players.RemoveAt(i);
                if (m_mapUserId2Address.ContainsKey(m_data.players[i].userId))
                {
                    m_mapUserId2Address.Remove(m_data.players[i].userId);
                }
            }
        }
    }


    private int GetPlayerIndexByUserId(uint userId)
    {
        for (int i = 0; i < m_data.players.Count; i++)
        {
            if (m_data.players[i].userId == userId)
            {
                return i;
            }
        }
        return -1;
    }


    private FSPPlayerData GetPlayerInfoByUserId(uint userId)
    {
        for (int i = 0; i < m_data.players.Count; i++)
        {
            if (m_data.players[i].userId == userId)
            {
                return m_data.players[i];
            }
        }
        return null;
    }

    private List<IPEndPoint> GetAllAddress()
    {
        List<IPEndPoint> list = new List<IPEndPoint>();
        for (int i = 0; i < m_data.players.Count; i++)
        {
            uint userId = m_data.players[i].userId;
            IPEndPoint address = m_mapUserId2Address[userId];
            list.Add(address);
        }

        return list;
    }

    private bool CanStartGame()
    {
        if (m_data.players.Count > 1 && IsAllReady())
        {
            return true;
        }
        return false;
    }

    private bool IsAllReady()
    {
        bool isAllReady = true;
        for (int i = 0; i < m_data.players.Count; i++)
        {
            if (!m_data.players[i].isReady)
            {
                isAllReady = false;
                break;
            }
        }
        return isAllReady;
    }


    private void SetReady(uint userId, bool value)
    {
        var info = GetPlayerInfoByUserId(userId);
        if (info != null)
        {
            info.isReady = value;
        }

    }

}
