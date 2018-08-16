using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public abstract class Singleton<T> where T:Singleton<T>,new ()
{
    private static T m_aInstance = default(T);

    public static T Instance
    {
        get
        {
            if(m_aInstance == null)
            {
                m_aInstance = new T();
                m_aInstance.InitSingleton();
            }
            return m_aInstance;
        }
    }

    protected virtual void InitSingleton()
    {

    }
}
