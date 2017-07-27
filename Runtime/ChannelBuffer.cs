#if ENABLE_UNET
using System;
using System.Collections.Generic;

namespace UnityEngine.Networking
{
    class ChannelBuffer : IDisposable
    {
        NetworkConnection m_Connection;

        ChannelPacket m_CurrentPacket;

        float m_LastFlushTime;

        byte m_ChannelId;
        int m_MaxPacketSize;
        bool m_IsReliable;
        bool m_IsBroken;
        int m_MaxPendingPacketCount;

        const int k_MaxFreePacketCount = 512; //  this is for all connections. maybe make this configurable
        const int k_MaxPendingPacketCount = 16;  // this is per connection. each is around 1400 bytes (MTU)

        Queue<ChannelPacket> m_PendingPackets;
        static List<ChannelPacket> s_FreePackets;
        static internal int pendingPacketCount; // this is across all connections. only used for profiler metrics.

        // config
        public float maxDelay = 0.01f;

        // stats
        float m_LastBufferedMessageCountTimer = Time.realtimeSinceStartup;

        public int numMsgsOut { get; private set; }
        public int numBufferedMsgsOut { get; private set; }
        public int numBytesOut { get; private set; }

        public int numMsgsIn { get; private set; }
        public int numBytesIn { get; private set; }

        public int numBufferedPerSecond { get; private set; }
        public int lastBufferedPerSecond { get; private set; }

        static NetworkWriter s_SendWriter = new NetworkWriter();

        // We need to reserve some space for header information, this will be taken off the total channel buffer size
        const int k_PacketHeaderReserveSize = 100;

        public ChannelBuffer(NetworkConnection conn, int bufferSize, byte cid, bool isReliable)
        {
            m_Connection = conn;
            m_MaxPacketSize = bufferSize - k_PacketHeaderReserveSize;
            m_CurrentPacket = new ChannelPacket(m_MaxPacketSize, isReliable);

            m_ChannelId = cid;
            m_MaxPendingPacketCount = k_MaxPendingPacketCount;
            m_IsReliable = isReliable;
            if (isReliable)
            {
                m_PendingPackets = new Queue<ChannelPacket>();
                if (s_FreePackets == null)
                {
                    s_FreePackets = new List<ChannelPacket>();
                }
            }
        }

        // Track whether Dispose has been called.
        bool m_Disposed;

        public void Dispose()
        {
            Dispose(true);
            // Take yourself off the Finalization queue
            // to prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!m_Disposed)
            {
                if (disposing)
                {
                    if (m_PendingPackets != null)
                    {
                        while (m_PendingPackets.Count > 0)
                        {
                            pendingPacketCount -= 1;

                            ChannelPacket packet = m_PendingPackets.Dequeue();
                            if (s_FreePackets.Count < k_MaxFreePacketCount)
                            {
                                s_FreePackets.Add(packet);
                            }
                        }
                        m_PendingPackets.Clear();
                    }
                }
            }
            m_Disposed = true;
        }

        public bool SetOption(ChannelOption option, int value)
        {
            switch (option)
            {
                case ChannelOption.MaxPendingBuffers:
                {
                    if (!m_IsReliable)
                    {
                        if (LogFilter.logError) { Debug.LogError("Cannot set MaxPendingBuffers on unreliable channel " + m_ChannelId); }
                        return false;
                    }
                    if (value < 0 || value >= k_MaxFreePacketCount)
                    {
                        if (LogFilter.logError) { Debug.LogError("Invalid MaxPendingBuffers for channel " + m_ChannelId + ". Must be greater than zero and less than " + k_MaxFreePacketCount); }
                        return false;
                    }
                    m_MaxPendingPacketCount = value;
                    return true;
                }
            }
            return false;
        }

        public void CheckInternalBuffer()
        {
            if (Time.realtimeSinceStartup - m_LastFlushTime > maxDelay && !m_CurrentPacket.IsEmpty())
            {
                SendInternalBuffer();
                m_LastFlushTime = Time.realtimeSinceStartup;
            }

            if (Time.realtimeSinceStartup - m_LastBufferedMessageCountTimer > 1.0f)
            {
                lastBufferedPerSecond = numBufferedPerSecond;
                numBufferedPerSecond = 0;
                m_LastBufferedMessageCountTimer = Time.realtimeSinceStartup;
            }
        }

        public bool SendWriter(NetworkWriter writer)
        {
            return SendBytes(writer.AsArraySegment().Array, writer.AsArraySegment().Count);
        }

        public bool Send(short msgType, MessageBase msg)
        {
            // build the stream
            s_SendWriter.StartMessage(msgType);
            msg.Serialize(s_SendWriter);
            s_SendWriter.FinishMessage();

            numMsgsOut += 1;
            return SendWriter(s_SendWriter);
        }

        internal bool SendBytes(byte[] bytes, int bytesToSend)
        {
#if UNITY_EDITOR
            UnityEditor.NetworkDetailStats.IncrementStat(
                UnityEditor.NetworkDetailStats.NetworkDirection.Outgoing,
                MsgType.HLAPIMsg, "msg", 1);
#endif
            if (bytesToSend <= 0)
            {
                // zero length packets getting into the packet queues are bad.
                if (LogFilter.logError) { Debug.LogError("ChannelBuffer:SendBytes cannot send zero bytes"); }
                return false;
            }

            // for fragmented channels, m_MaxPacketSize is set to the max size of a fragmented packet, so anything higher than this should fail for any kind of channel.
            if (bytesToSend > m_MaxPacketSize)
            {
                if (LogFilter.logError) { Debug.LogError("Failed to send big message of " + bytesToSend + " bytes. The maximum is " + m_MaxPacketSize + " bytes on this channel."); }
                return false;
            }

            if (!m_CurrentPacket.HasSpace(bytesToSend))
            {
                if (m_IsReliable)
                {
                    if (m_PendingPackets.Count == 0)
                    {
                        // nothing in the pending queue yet, just flush and write
                        if (!m_CurrentPacket.SendToTransport(m_Connection, m_ChannelId))
                        {
                            QueuePacket();
                        }
                        m_CurrentPacket.Write(bytes, bytesToSend);
                        return true;
                    }

                    if (m_PendingPackets.Count >= m_MaxPendingPacketCount)
                    {
                        if (!m_IsBroken)
                        {
                            // only log this once, or it will spam the log constantly
                            if (LogFilter.logError) { Debug.LogError("ChannelBuffer buffer limit of " + m_PendingPackets.Count + " packets reached."); }
                        }
                        m_IsBroken = true;
                        return false;
                    }

                    // calling SendToTransport here would write out-of-order data to the stream. just queue
                    QueuePacket();
                    m_CurrentPacket.Write(bytes, bytesToSend);
                    return true;
                }

                if (!m_CurrentPacket.SendToTransport(m_Connection, m_ChannelId))
                {
                    if (LogFilter.logError) { Debug.Log("ChannelBuffer SendBytes no space on unreliable channel " + m_ChannelId); }
                    return false;
                }

                m_CurrentPacket.Write(bytes, bytesToSend);
                return true;
            }

            m_CurrentPacket.Write(bytes, bytesToSend);
            if (maxDelay == 0.0f)
            {
                return SendInternalBuffer();
            }
            return true;
        }

        void QueuePacket()
        {
            pendingPacketCount += 1;
            m_PendingPackets.Enqueue(m_CurrentPacket);
            m_CurrentPacket = AllocPacket();
        }

        ChannelPacket AllocPacket()
        {
#if UNITY_EDITOR
            UnityEditor.NetworkDetailStats.SetStat(
                UnityEditor.NetworkDetailStats.NetworkDirection.Outgoing,
                MsgType.HLAPIPending, "msg", pendingPacketCount);
#endif
            if (s_FreePackets.Count == 0)
            {
                return new ChannelPacket(m_MaxPacketSize, m_IsReliable);
            }

            var packet = s_FreePackets[s_FreePackets.Count - 1];
            s_FreePackets.RemoveAt(s_FreePackets.Count - 1);

            packet.Reset();
            return packet;
        }

        static void FreePacket(ChannelPacket packet)
        {
#if UNITY_EDITOR
            UnityEditor.NetworkDetailStats.SetStat(
                UnityEditor.NetworkDetailStats.NetworkDirection.Outgoing,
                MsgType.HLAPIPending, "msg", pendingPacketCount);
#endif
            if (s_FreePackets.Count >= k_MaxFreePacketCount)
            {
                // just discard this packet, already tracking too many free packets
                return;
            }
            s_FreePackets.Add(packet);
        }

        public bool SendInternalBuffer()
        {
#if UNITY_EDITOR
            UnityEditor.NetworkDetailStats.IncrementStat(
                UnityEditor.NetworkDetailStats.NetworkDirection.Outgoing,
                MsgType.LLAPIMsg, "msg", 1);
#endif
            if (m_IsReliable && m_PendingPackets.Count > 0)
            {
                // send until transport can take no more
                while (m_PendingPackets.Count > 0)
                {
                    var packet = m_PendingPackets.Dequeue();
                    if (!packet.SendToTransport(m_Connection, m_ChannelId))
                    {
                        m_PendingPackets.Enqueue(packet);
                        break;
                    }
                    pendingPacketCount -= 1;
                    FreePacket(packet);

                    if (m_IsBroken && m_PendingPackets.Count < (m_MaxPendingPacketCount / 2))
                    {
                        if (LogFilter.logWarn) { Debug.LogWarning("ChannelBuffer recovered from overflow but data was lost."); }
                        m_IsBroken = false;
                    }
                }
                return true;
            }
            return m_CurrentPacket.SendToTransport(m_Connection, m_ChannelId);
        }
    }
}
#endif //ENABLE_UNET
