namespace VelNet
{
	public interface IPackState
	{
		public void PackState(NetworkWriter writer);
		public void UnpackState(NetworkReader reader);
	}
}
