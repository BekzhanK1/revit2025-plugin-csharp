using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using SBS.DTO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;

namespace SBS.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SbsWallsCommand : BaseCommand
    {
        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);
            //var view = new LevelView(doc);
            //view.ShowDialog();
            GetWallsData(commandData);
            return Result.Succeeded;
        }

        private void GetWallsData(ExternalCommandData commandData)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var result = new List<WallInfoDto>();
            var allPlacedWalls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))                 // берём только элементы класса Wall
                .WhereElementIsNotElementType()        // исключаем типы, оставляем только экземпляры
                .Cast<Wall>().ToList();

            foreach (var wall in allPlacedWalls)
            {
                var id = wall.Id.IntegerValue;
                var baseConstraint = wall.LookupParameter("Зависимость снизу")?.AsValueString();
                var topConstraint = wall.LookupParameter("Зависимость сверху")?.AsValueString();
                var baseOffsetFeet = wall.LookupParameter("Смещение снизу")?.AsDouble();
                var baseOffset = baseOffsetFeet.HasValue ? UnitUtils.ConvertFromInternalUnits(baseOffsetFeet.Value, UnitTypeId.Millimeters) : 0;
                var topOffsetFeet = wall.LookupParameter("Смещение сверху")?.AsDouble();
                var topOffset = topOffsetFeet.HasValue ? UnitUtils.ConvertFromInternalUnits(topOffsetFeet.Value, UnitTypeId.Millimeters) : 0;
                var biLevel = wall.LookupParameter("BI_этаж")?.AsValueString();
                var mark = wall.LookupParameter("Марка")?.AsString();
                var unconnectedHeightFeet = wall.LookupParameter("Неприсоединенная высота").AsDouble();
                var unconnectedHeight = UnitUtils.ConvertFromInternalUnits(unconnectedHeightFeet, UnitTypeId.Millimeters);
                var lengthString = wall.LookupParameter("Длина")?.AsValueString().Split(' ')[0];
                var areaString = wall.LookupParameter("Площадь")?.AsValueString().Split(' ')[0];
                var volumeString = wall.LookupParameter("Объем")?.AsValueString().Split(' ')[0];
                var length = string.IsNullOrEmpty(lengthString) ? 0 : double.Parse(lengthString.Replace(".", ","));
                var area = string.IsNullOrEmpty(areaString) ? 0 : double.Parse(areaString.Replace(".", ","));
                var volume = string.IsNullOrEmpty(volumeString) ? 0 : double.Parse(volumeString.Replace(".", ","));
                var type = doc.GetElement(wall.GetTypeId()) as WallType;
                var wallKind = type?.Kind;
                string typeName = type?.Name;
                var modelGroup = type.LookupParameter("Группа модели")?.AsString();
                double wallWidth = UnitUtils.ConvertFromInternalUnits(type.Width, UnitTypeId.Millimeters);
                var compoundStructure = type?.GetCompoundStructure();
                var layersDto = new List<WallStructureDto>();
                if (compoundStructure != null)
                {
                    var layers = compoundStructure.GetLayers();
                    foreach (var layer in layers)
                    {
                        var matId = layer.MaterialId;
                        var mat = doc.GetElement(matId) as Material;
                        string matName = mat?.Name ?? "Нет материала";
                        double matWidth = UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Millimeters);
                        var dto = new WallStructureDto()
                        {
                            MaterialName = matName,
                            MaterialWidth = matWidth,
                        };
                        layersDto.Add(dto);
                    }
                }

                var geometry = GetGeom(wall);
                var wallInfoDto = new WallInfoDto()
                {
                    Id = id,
                    BaseConstraint = baseConstraint,
                    TopConstraint = topConstraint,
                    BaseOffset = baseOffset,
                    TopOffset = topOffset,
                    BiLevel = biLevel,
                    Mark = mark,
                    UnconnectedHeight = unconnectedHeight,
                    Length = length,
                    Area = area,
                    Volume = volume,
                    TypeName = typeName,
                    WallKind = wallKind.ToString(),
                    ModelGroup = modelGroup,
                    WallWidth = wallWidth,
                    Layers = layersDto,
                    WallLines = geometry,
                };
                result.Add(wallInfoDto);
            }

            if (result.Any())
            {
                var json = JsonConvert.SerializeObject(result);
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Текстовый файл|*.txt|JSON файл|*.json";
                    sfd.Title = "Сохранить результат";
                    sfd.FileName = "walls.json";

                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        File.WriteAllText(sfd.FileName, json, Encoding.UTF8);
                    }
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Нет стен для выгрузки");
            }
        }

        private List<WallLinesDto> GetGeom(Wall wall)
        {
            var result = new List<WallLinesDto>();
            Options opt = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = true
            };

            GeometryElement geomElem = wall.get_Geometry(opt);
            List<string> lines = new List<string>();
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid && solid.Volume > 0) // только реальные solid
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        Curve curve = edge.AsCurve();
                        XYZ p1 = curve.GetEndPoint(0);
                        XYZ p2 = curve.GetEndPoint(1);
                        var dto = new WallLinesDto()
                        {
                            Point1 = p1,
                            Point2 = p2,
                        };
                        result.Add(dto);
                    }
                }
                else if (geomObj is Curve curve)
                {
                    XYZ p1 = curve.GetEndPoint(0);
                    XYZ p2 = curve.GetEndPoint(1);
                    var dto = new WallLinesDto()
                    {
                        Point1 = p1,
                        Point2 = p2,
                    };
                    result.Add(dto);
                }
            }

            return result;
        }
    }
}
