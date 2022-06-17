using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VelNet
{
	public abstract class NetworkComponent : MonoBehaviour
	{
		public NetworkObject networkObject;
		protected bool IsMine => networkObject != null && networkObject.owner != null && networkObject.owner.isLocal;
		protected VelNetPlayer Owner => networkObject != null ? networkObject.owner : null;

		/// <summary>
		/// call this in child classes to send a message to other people
		/// </summary>
		protected void SendBytes(byte[] message, bool reliable = true)
		{
			networkObject.SendBytes(this, false, message, reliable);
		}

		/// <summary>
		/// call this in child classes to send a message to other people
		/// </summary>
		protected void SendBytesToGroup(string group, byte[] message, bool reliable = true)
		{
			networkObject.SendBytesToGroup(this, false, group, message, reliable);
		}

		/// <summary>
		/// This is called by <see cref="NetworkObject"/> when messages are received for this component
		/// </summary>
		public abstract void ReceiveBytes(byte[] message);

		public void ReceiveRPC(byte[] message)
		{
			using MemoryStream mem = new MemoryStream(message);
			using BinaryReader reader = new BinaryReader(mem);
			byte methodIndex = reader.ReadByte();
			int length = reader.ReadInt32();
			byte[] parameterData = reader.ReadBytes(length);

			MethodInfo[] mInfos = GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
			Array.Sort(mInfos, (m1, m2) => string.Compare(m1.Name, m2.Name, StringComparison.Ordinal));
			try
			{
				mInfos[methodIndex].Invoke(this, length > 0 ? new object[] { parameterData } : Array.Empty<object>());
			}
			catch (Exception e)
			{
				Debug.LogError($"Error processing received RPC {e}");
			}
		}

		protected void SendRPCToGroup(string group, bool runLocally, string methodName, byte[] parameterData = null)
		{
			if (GenerateRPC(methodName, parameterData, out byte[] bytes)) return;

			if (runLocally) ReceiveRPC(bytes);

			networkObject.SendBytesToGroup(this, true, group, bytes, true);
		}

		protected void SendRPC(string methodName, bool runLocally, byte[] parameterData = null)
		{
			if (GenerateRPC(methodName, parameterData, out byte[] bytes)) return;

			if (networkObject.SendBytes(this, true, bytes, true))
			{
				// only run locally if we can successfully send
				if (runLocally) ReceiveRPC(bytes);
			}
		}

		private bool GenerateRPC(string methodName, byte[] parameterData, out byte[] bytes)
		{
			bytes = null;
			using MemoryStream mem = new MemoryStream();
			using BinaryWriter writer = new BinaryWriter(mem);

			MethodInfo[] mInfos = GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
			Array.Sort(mInfos, (m1, m2) => string.Compare(m1.Name, m2.Name, StringComparison.Ordinal));
			int methodIndex = mInfos.ToList().FindIndex(m => m.Name == methodName);
			switch (methodIndex)
			{
				case > 255:
					Debug.LogError("Too many methods in this class.");
					return true;
				case < 0:
					Debug.LogError("Can't find a method with that name.");
					return true;
			}

			writer.Write((byte)methodIndex);
			if (parameterData != null)
			{
				writer.Write(parameterData.Length);
				writer.Write(parameterData);
			}
			else
			{
				writer.Write(0);
			}

			bytes = mem.ToArray();

			return false;
		}
	}
}