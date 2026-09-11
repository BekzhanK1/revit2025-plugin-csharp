namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// Опции синхронизации материалов. Для init — строгий режим: любая ошибка блокирует продолжение.
    /// </summary>
    public sealed class RevitMaterialsSyncOptions
    {
        /// <summary>
        /// Проверить SR_ID в RFA и наличие surface-типов в surfaces.rvt до импорта в проект.
        /// </summary>
        public bool ValidateSrIdBeforeImport { get; init; }

        /// <summary>
        /// При ошибках скачивания/валидации не импортировать материалы в документ.
        /// </summary>
        public bool AbortBeforeImportOnErrors { get; init; }

        public static RevitMaterialsSyncOptions StrictInit { get; } = new RevitMaterialsSyncOptions
        {
            ValidateSrIdBeforeImport = true,
            AbortBeforeImportOnErrors = true
        };

        /// <summary>
        /// Init после «игнорировать preflight»: SR_ID не проверяем до импорта, но скачивание должно пройти.
        /// </summary>
        public static RevitMaterialsSyncOptions InitSkipSrIdValidation { get; } = new RevitMaterialsSyncOptions
        {
            ValidateSrIdBeforeImport = false,
            AbortBeforeImportOnErrors = true
        };
    }
}
