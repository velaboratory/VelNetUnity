using System;
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

		private MethodInfo[] methodInfos;
		private NetworkWriter rpcWriter;

		// Cached send header: [ObjectSync byte][encoded networkId][componentByte]
		// Built once on first send, never changes. One for non-RPC, one for RPC.
		internal byte[] cachedSendHeader;
		internal byte[] cachedRpcSendHeader;
		internal int cachedSendHeaderLength;
		private int cachedComponentIndex = -1;

		/// <summary>
		/// Returns the cached component index within networkObject.syncedComponents.
		/// Computed once on first call and cached. Also builds the full send headers. Returns -1 on error.
		/// </summary>
		internal int GetCachedComponentIndex()
		{
			if (cachedComponentIndex >= 0) return cachedComponentIndex;

			var components = networkObject.syncedComponents;
			int count = components.Count;
			for (int i = 0; i < count; i++)
			{
				if (ReferenceEquals(components[i], this))
				{
					cachedComponentIndex = i;

					// Build the full cached headers: [ObjectSync][networkId][componentByte]
					NetworkWriter temp = VelNetManager.GetTempWriter();
					temp.Reset();
					temp.Write((byte)VelNetManager.MessageType.ObjectSync);
					temp.Write(networkObject.networkId);
					temp.Write((byte)(i << 1)); // non-RPC component byte
					cachedSendHeaderLength = temp.Length;
					cachedSendHeader = new byte[cachedSendHeaderLength];
					System.Array.Copy(temp.Buffer, cachedSendHeader, cachedSendHeaderLength);

					// RPC header is identical except last byte has bit 0 set
					cachedRpcSendHeader = new byte[cachedSendHeaderLength];
					System.Array.Copy(cachedSendHeader, cachedRpcSendHeader, cachedSendHeaderLength);
					cachedRpcSendHeader[cachedSendHeaderLength - 1] |= 1;

					return i;
				}
			}

			return -1;
		}

		/// <summary>
		/// call this in child classes to send a message to other people
		/// </summary>
		protected void SendBytes(byte[] message, bool reliable = true)
		{
			networkObject.SendBytes(this, false, message, 0, message.Length, reliable);
		}

		/// <summary>
		/// call this in child classes to send a message from a buffer region
		/// </summary>
		protected void SendBytes(byte[] buffer, int offset, int length, bool reliable = true)
		{
			networkObject.SendBytes(this, false, buffer, offset, length, reliable);
		}

		/// <summary>
		/// call this in child classes to send a message to other people
		/// </summary>
		protected void SendBytesToGroup(string group, byte[] message, bool reliable = true)
		{
			networkObject.SendBytesToGroup(this, false, group, message, 0, message.Length, reliable);
		}

		/// <summary>
		/// call this in child classes to send a message to a group from a buffer region
		/// </summary>
		protected void SendBytesToGroup(string group, byte[] buffer, int offset, int length, bool reliable = true)
		{
			networkObject.SendBytesToGroup(this, false, group, buffer, offset, length, reliable);
		}

		/// <summary>
		/// This is called by <see cref="NetworkObject"/> when messages are received for this component
		/// </summary>
		public abstract void ReceiveBytes(NetworkReader reader);

		public void ReceiveRPC(NetworkReader reader)
		{
			byte methodIndex = reader.ReadByte();
			int length = reader.ReadInt32();
			// Read parameter data as a segment to avoid allocation when possible
			byte[] parameterData = length > 0 ? reader.ReadBytes(length) : null;

			if (methodInfos == null)
			{
				methodInfos = GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
				Array.Sort(methodInfos, (m1, m2) => string.Compare(m1.Name, m2.Name, StringComparison.Ordinal));
			}

			try
			{
				methodInfos[methodIndex].Invoke(this, length > 0 ? new object[] { parameterData } : Array.Empty<object>());
			}
			catch (Exception e)
			{
				Debug.LogError($"Error processing received RPC {e}");
			}
		}

		protected void SendRPCToGroup(string group, bool runLocally, string methodName, byte[] parameterData = null)
		{
			if (!GenerateRPC(methodName, parameterData, out byte[] buffer, out int length)) return;

			if (runLocally)
			{
				var rpcReader = new NetworkReader(buffer, 0, length);
				ReceiveRPC(rpcReader);
			}

			networkObject.SendBytesToGroup(this, true, group, buffer, 0, length, true);
		}

		protected void SendRPC(string methodName, bool runLocally, byte[] parameterData = null)
		{
			if (!GenerateRPC(methodName, parameterData, out byte[] buffer, out int length)) return;

			if (networkObject.SendBytes(this, true, buffer, 0, length, true))
			{
				// only run locally if we can successfully send
				if (runLocally)
				{
					var rpcReader = new NetworkReader(buffer, 0, length);
					ReceiveRPC(rpcReader);
				}
			}
		}

		/// <summary>
		/// Generates an RPC message into the reusable rpcWriter buffer.
		/// Returns true on success, false on failure.
		/// </summary>
		private bool GenerateRPC(string methodName, byte[] parameterData, out byte[] buffer, out int length)
		{
			buffer = null;
			length = 0;

			if (rpcWriter == null) rpcWriter = new NetworkWriter(64);
			rpcWriter.Reset();

			if (methodInfos == null)
			{
				methodInfos = GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
				Array.Sort(methodInfos, (m1, m2) => string.Compare(m1.Name, m2.Name, StringComparison.Ordinal));
			}

			int methodIndex = methodInfos.ToList().FindIndex(m => m.Name == methodName);
			switch (methodIndex)
			{
				case > 255:
					Debug.LogError("Too many methods in this class.");
					return false;
				case < 0:
					Debug.LogError("Can't find a method with that name.");
					return false;
			}

			rpcWriter.Write((byte)methodIndex);
			if (parameterData != null)
			{
				rpcWriter.Write(parameterData.Length);
				rpcWriter.Write(parameterData);
			}
			else
			{
				rpcWriter.Write(0);
			}

			buffer = rpcWriter.Buffer;
			length = rpcWriter.Length;
			return true;
		}
	}
}
