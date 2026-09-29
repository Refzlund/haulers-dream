using Verse;

namespace HaulersDream
{
	internal sealed class RefuelSplit
	{
		internal readonly Thing Source;

		internal readonly int Requested;

		internal readonly int BeforeCount;

		internal int NativeBeforeCount;

		internal Thing Piece;

		internal bool DebitObserved;

		internal bool HasFundedPiece
		{
			get
			{
				if (Piece != null)
				{
					if (Piece != Source)
					{
						return DebitObserved;
					}
					return Requested == BeforeCount;
				}
				return false;
			}
		}

		internal RefuelSplit(Thing source, int count)
		{
			Source = source;
			Requested = count;
			BeforeCount = source.stackCount;
			if (count == BeforeCount)
			{
				Piece = source;
			}
		}
	}
}
