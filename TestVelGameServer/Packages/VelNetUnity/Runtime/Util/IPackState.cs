namespace VelNet
{
	public interface IPackState
	{
		public byte[] PackState();
		public void UnpackState(byte[] state);
	}
}