using System.Collections.Generic;

namespace LevelEditorPlugin.Entities
{
	[EntityBinding(DataType = typeof(FrostySdk.Ebx.DynamicEnlightenEntityData))]
	public class DynamicEnlightenEntity : EnlightenEntity, IEntityData<FrostySdk.Ebx.DynamicEnlightenEntityData>
	{
		public new FrostySdk.Ebx.DynamicEnlightenEntityData Data => data as FrostySdk.Ebx.DynamicEnlightenEntityData;
		public override string DisplayName => "DynamicEnlighten";
		public override FrostySdk.Ebx.Realm Realm => Data.Realm;

		public DynamicEnlightenEntity(FrostySdk.Ebx.DynamicEnlightenEntityData inData, Entity inParent)
			: base(inData, inParent)
		{
		}
	}
}

