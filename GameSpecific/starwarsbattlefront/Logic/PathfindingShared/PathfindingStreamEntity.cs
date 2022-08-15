using System.Collections.Generic;

namespace LevelEditorPlugin.Entities
{
	[EntityBinding(DataType = typeof(FrostySdk.Ebx.PathfindingStreamEntityData))]
	public class PathfindingStreamEntity : LogicEntity, IEntityData<FrostySdk.Ebx.PathfindingStreamEntityData>
	{
		public new FrostySdk.Ebx.PathfindingStreamEntityData Data => data as FrostySdk.Ebx.PathfindingStreamEntityData;
		public override string DisplayName => "PathfindingStream";

		public PathfindingStreamEntity(FrostySdk.Ebx.PathfindingStreamEntityData inData, Entity inParent)
			: base(inData, inParent)
		{
		}
	}
}

