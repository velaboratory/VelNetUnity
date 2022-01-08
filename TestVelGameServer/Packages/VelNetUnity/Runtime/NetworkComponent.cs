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
			networkObject.SendBytes(this, message, reliable);
		}
		
		/// <summary>
		/// call this in child classes to send a message to other people
		/// </summary>
		protected void SendBytesToGroup(string group, byte[] message, bool reliable = true)
		{
			networkObject.SendBytesToGroup(this, group, message, reliable);
		}

		/// <summary>
		/// This is called by <see cref="NetworkObject"/> when messages are received for this component
		/// </summary>
		public abstract void ReceiveBytes(byte[] message);
	}
}