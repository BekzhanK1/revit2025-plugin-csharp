using System;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class TbaDto
    {
        public Guid projectId { get; set; }
        public string ProjectName { get; set; }
        public string BuildingClass { get; set; }
        public int Code { get; set; }
        public string GipPO { get; set; }
        public string DrawingPackage { get; set; } //ПЧ
        public string Section { get; set; }
        public int? Stages { get; set; }
        public double? TotalArea { get; set; }
        public double? RealizableArea { get; set; }
        public string FoundationType { get; set; } //Тип фундамента
        public double? CeilingHeight { get; set; } //Высота типового этажа
        //Расходы материалов по конструктивам
        public double? ConsumptionOfConcreteForVerticalSupportingStructures { get; set; } // Вертикальные несущие конструкции (колонны, пилоны и стены) - бетон
        public double? ConsumptionOfReinforcementForVerticalSupportingStructures { get; set; } // Вертикальные несущие конструкции (колонны, пилоны и стены) - арматура
        public double? ConsumptionOfConcreteForStairs { get; set; } // Лестницы - бетон
        public double? ConsumptionOfReinforcementForStairs { get; set; } // Лестницы - арматура
        public double? ConsumptionOfConcreteForFoundationSlabs { get; set; } // Фундамент - бетон
        public double? ConsumptionOfReinforcementForFoundationSlabs { get; set; } // Фундамент - арматура
        public double? ConsumptionOfConcreteForFloorSlabsAndCoatingsParapets { get; set; } // Плиты перекрытий и покрытия, парапеты - бетон
        public double? ConsumptionOfReinforcementForFloorSlabsAndCoatingsParapets { get; set; } // Плиты перекрытий и покрытия, парапеты - арматура
        public double? ConsumptionOfConcreteForFloorSlabs { get; set; } // Плиты пола по грунту - бетон
        public double? ConsumptionOfReinforcementForFloorSlabs { get; set; } // Плиты пола по грунту - арматура

        //Расходы материалов (расчет)
        public double? ReinforcementConsumptionForTheSupportingFramePlan { get; set; } // Средний расход арматуры на несущий каркас, кг/м3
        public double? SpecificConsumptionOfReinforcementPerConcreteRecommended { get; set; }//Рекомендуемый показатель
        public double? SpecificConsumptionOfConcreteForTheTotalAreaPlan { get; set; }//Удельный расход бетона на общую площадь, м3/м2
        public double? SpecificConsumptionOfConcreteRecommended { get; set; }//Рекомендуемый показатель
        public double? SpecificConsumptionOfConcreteOfTheSoldAreaPlan { get; set; }//Удельный расход бетона на реализуемую площадь, м3/м2
        public double? ConcreteConsumptionPerSoldAreaRecommended { get; set; }//Рекомендуемый показатель
        public double? SpecificConsumptionOfReinforcementForTheTotalAreaPlan { get; set; }//Удельный расход арматуры на общую площадь, кг/м2
        public double? SpecificConsumptionOfForTheTotalAreaRecommended { get; set; }//Рекомендуемый показатель
        public double? ReinforcementConsumptionPerSoldAreaPlan { get; set; }//Удельный расход арматуры на реализуемую площадь, кг/м2
        public double? ReinforcementConsumptionPerSoldAreaRecommended { get; set; }//Рекомендуемый показатель
        public List<BimDetailDto> BimDetails { get; set; } // BIM Детальные данные проекта
    }

    public class BimDetailDto
    {
        public string Brand { get; set; } // Марка, привет от гугл переводчика!
        public double Volume { get; set; } // Объем
    }
}
