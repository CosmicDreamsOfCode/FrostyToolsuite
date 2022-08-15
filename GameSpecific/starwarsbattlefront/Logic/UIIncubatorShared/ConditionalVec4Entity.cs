using System.Collections.Generic;

namespace LevelEditorPlugin.Entities
{
	[EntityBinding(DataType = typeof(FrostySdk.Ebx.ConditionalVec4EntityData))]
	public class ConditionalVec4Entity : ConditionalStateEntity, IEntityData<FrostySdk.Ebx.ConditionalVec4EntityData>
	{
		public new FrostySdk.Ebx.ConditionalVec4EntityData Data => data as FrostySdk.Ebx.ConditionalVec4EntityData;
		public override string DisplayName => "ConditionalVec4";
		public override FrostySdk.Ebx.Realm Realm => Data.Realm;

		public ConditionalVec4Entity(FrostySdk.Ebx.ConditionalVec4EntityData inData, Entity inParent)
			: base(inData, inParent)
		{
		}
	}
}

