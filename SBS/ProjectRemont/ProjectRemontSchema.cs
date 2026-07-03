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

        public static readonly Guid SchemaGuid = new Guid("171500a5-1d6b-4f5d-8253-e53b5a8275c3");

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
