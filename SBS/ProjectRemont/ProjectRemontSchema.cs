using Autodesk.Revit.DB.ExtensibleStorage;
using System;

namespace SmartRemont.ExportRooms.ProjectRemont
{
    public static class ProjectRemontSchema
    {
        public const string FieldRemontId = "remont_id";
        public const string FieldClientRequestId = "client_request_id";
        public const string FieldInitializedAt = "initialized_at";
        public const string FieldPluginVersion = "plugin_version";

        /// <summary>Должен совпадать с &lt;AddInId&gt; в deploy/SmartRemont.ExportRooms.addin.</summary>
        public static readonly Guid PluginAddInId = new Guid("B9E4D1C2-3A5F-4E7B-9D0E-1F2A3B4C5D6E");

        public static readonly Guid SchemaGuid = new Guid("a8f3c2e1-4b5d-4e6f-9a0b-1c2d3e4f5a6b");

        /// <summary>Первая версия схемы без SetApplicationGUID — только чтение legacy-проектов.</summary>
        public static readonly Guid LegacySchemaGuid = new Guid("171500a5-1d6b-4f5d-8253-e53b5a8275c3");

        const string SchemaName = "SmartRemontProjectRemont";
        const string VendorId = "SmartRemont";

        static readonly object SchemaLock = new object();
        static Schema _schema;

        public static Schema GetOrCreateSchema()
        {
            if (_schema != null)
                return _schema;

            lock (SchemaLock)
            {
                if (_schema != null)
                    return _schema;

                _schema = Schema.Lookup(SchemaGuid);
                if (_schema != null)
                    return _schema;

                var builder = new SchemaBuilder(SchemaGuid);
                builder.SetSchemaName(SchemaName);
                builder.SetReadAccessLevel(AccessLevel.Public);
                builder.SetWriteAccessLevel(AccessLevel.Vendor);
                builder.SetVendorId(VendorId);
                builder.SetApplicationGUID(PluginAddInId);
                builder.SetDocumentation("Smart Remont project initialization metadata on ProjectInformation.");

                builder.AddSimpleField(FieldRemontId, typeof(int));
                builder.AddSimpleField(FieldClientRequestId, typeof(int));
                builder.AddSimpleField(FieldInitializedAt, typeof(string));
                builder.AddSimpleField(FieldPluginVersion, typeof(string));

                _schema = builder.Finish();
                return _schema;
            }
        }
    }
}
