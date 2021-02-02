using System;
using UnityEngine;

namespace LightNet
{
    /// <summary>
    /// A neat helper for easier unity data serialization
    /// </summary>
    public static class SerializationHelper
    {
        public static void FromBytes(byte[] data, ref int offset, ref bool value)
        {
            Debug.Assert(offset + sizeof(byte) <= data.Length);
            value = data[offset] != 0;
            offset += sizeof(byte);
        }

        public static void FromBytes(byte[] data, ref int offset, ref float value)
        {
            Debug.Assert(offset + sizeof(float) <= data.Length);
            value = BitConverter.ToSingle(data, offset);
            offset += sizeof(float);
        }

        public static void FromBytes(byte[] data, ref int offset, ref Vector3 vector)
        {
            FromBytes(data, ref offset, ref vector.x);
            FromBytes(data, ref offset, ref vector.y);
            FromBytes(data, ref offset, ref vector.z);
        }

        public static void FromBytes(byte[] data, ref int offset, ref Quaternion quat)
        {
            FromBytes(data, ref offset, ref quat.x);
            FromBytes(data, ref offset, ref quat.y);
            FromBytes(data, ref offset, ref quat.z);
            FromBytes(data, ref offset, ref quat.w);
        }

        public static void ToBytes(ulong value, byte[] data, ref int offset)
        {
            Debug.Assert(offset + sizeof(float) < data.Length);
            byte[] buffer = BitConverter.GetBytes(value);
            Array.Copy(buffer, 0, data, offset, buffer.Length);
            offset += buffer.Length;
        }

        public static void ToBytes(bool value, byte[] data, ref int offset)
        {
            Debug.Assert(offset + sizeof(bool) <= data.Length);
            data[offset] = value ? (byte)1 : (byte)0;
            offset += sizeof(byte);
        }

        public static void ToBytes(float value, byte[] data, ref int offset)
        {
            Debug.Assert(offset + sizeof(float) <= data.Length);
            byte[] buffer = BitConverter.GetBytes(value);
            Array.Copy(buffer, 0, data, offset, buffer.Length);
            offset += buffer.Length;
        }

        public static void ToBytes(ref Vector3 vector, byte[] data, ref int offset)
        {
            ToBytes(vector.x, data, ref offset);
            ToBytes(vector.y, data, ref offset);
            ToBytes(vector.z, data, ref offset);
        }

        public static void ToBytes(ref Quaternion quat, byte[] data, ref int offset)
        {
            ToBytes(quat.x, data, ref offset);
            ToBytes(quat.y, data, ref offset);
            ToBytes(quat.z, data, ref offset);
            ToBytes(quat.w, data, ref offset);
        }
    }

    /// <summary>
    /// Interface that needs to be implemented for each of your custom 
    /// network data types / package types you wish to send over the network.<br/><br/>
    /// IMPORTANT NOTE: The first byte ALWAYS corresponds to the custom package type and should be handled as such!<br/>
    /// See: <see cref="ReceivedNetworkDataEventArgs.Type"/>
    /// </summary>
    public interface NetworkData
    {
        byte[] Serialize();
        void Deserialize(byte[] data);
    }
}