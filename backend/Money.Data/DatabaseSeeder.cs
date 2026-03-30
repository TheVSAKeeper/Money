using Money.Data.Entities;

namespace Money.Data;

public static class DatabaseSeeder
{
    public static List<Category> SeedCategories(int userId, out int lastIndex, int startIndex = 1)
    {
        var categories = new List<Category>
        {
            new()
            {
                UserId = userId,
                Name = "Продукты",
                Description = "Расходы на продукты питания",
                TypeId = 1,
                Color = "#FFCC00",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Фрукты и овощи",
                        Description = "Расходы на свежие фрукты и овощи",
                        TypeId = 1,
                        Color = "#FF9900",
                    },

                    new()
                    {
                        UserId = userId,
                        Name = "Мясо и рыба",
                        Description = "Расходы на мясные и рыбные продукты",
                        TypeId = 1,
                        Color = "#CC3300",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Транспорт",
                Description = "Расходы на транспорт",
                TypeId = 1,
                Color = "#0099CC",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Общественный транспорт",
                        Description = "Расходы на проезд в общественном транспорте",
                        TypeId = 1,
                        Color = "#007ACC",
                    },

                    new()
                    {
                        UserId = userId,
                        Name = "Такси",
                        Description = "Расходы на такси",
                        TypeId = 1,
                        Color = "#005B99",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Развлечения",
                Description = "Расходы на развлечения и досуг",
                TypeId = 1,
                Color = "#FF3366",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Кино",
                        Description = "Расходы на посещение кинотеатров",
                        TypeId = 1,
                        Color = "#FF3366",
                    },

                    new()
                    {
                        UserId = userId,
                        Name = "Концерты",
                        Description = "Расходы на посещение концертов",
                        TypeId = 1,
                        Color = "#FF6699",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Коммунальные услуги",
                Description = "Расходы на коммунальные услуги",
                TypeId = 1,
                Color = "#66CC66",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Электричество",
                        Description = "Расходы на электроэнергию",
                        TypeId = 1,
                        Color = "#FFCC00",
                    },

                    new()
                    {
                        UserId = userId,
                        Name = "Вода",
                        Description = "Расходы на водоснабжение",
                        TypeId = 1,
                        Color = "#66CCFF",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Одежда",
                Description = "Расходы на одежду и обувь",
                TypeId = 1,
                Color = "#FF6600",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Одежда для детей",
                        Description = "Расходы на детскую одежду",
                        TypeId = 1,
                        Color = "#FF9966",
                    },

                    new()
                    {
                        UserId = userId,
                        Name = "Одежда для взрослых",
                        Description = "Расходы на одежду для взрослых",
                        TypeId = 1,
                        Color = "#CC6600",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Здоровье",
                Description = "Расходы на медицинские услуги и лекарства",
                TypeId = 1,
                Color = "#CC33FF",
            },

            new()
            {
                UserId = userId,
                Name = "Образование",
                Description = "Расходы на обучение и курсы",
                TypeId = 1,
                Color = "#FFCCFF",
            },

            new()
            {
                UserId = userId,
                Name = "Зарплата",
                Description = "Основной источник дохода",
                TypeId = 2,
                Color = "#66B3FF",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Бонусы",
                        Description = "Бонусы от работодателя",
                        TypeId = 2,
                        Color = "#66B3FF",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Дополнительный доход",
                Description = "Доход от фриланса и подработок",
                TypeId = 2,
                Color = "#FFB366",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Фриланс",
                        Description = "Доход от фриланс-проектов",
                        TypeId = 2,
                        Color = "#FFB366",
                    },

                    new()
                    {
                        UserId = userId,
                        Name = "Курсы и тренинги",
                        Description = "Доход от проведения курсов и тренингов",
                        TypeId = 2,
                        Color = "#FFB3FF",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Инвестиции",
                Description = "Доход от инвестиций",
                TypeId = 2,
                Color = "#66FF66",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Дивиденды",
                        Description = "Доход от акций и инвестиций",
                        TypeId = 2,
                        Color = "#FF6666",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Пассивный доход",
                Description = "Доход от аренды и других источников",
                TypeId = 2,
                Color = "#FF6666",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Аренда недвижимости",
                        Description = "Доход от аренды квартир или домов",
                        TypeId = 2,
                        Color = "#66FF66",
                    },
                ],
            },

            new()
            {
                UserId = userId,
                Name = "Премии",
                Description = "Дополнительные выплаты и бонусы",
                TypeId = 2,
                Color = "#FFCC00",
                SubCategories =
                [
                    new()
                    {
                        UserId = userId,
                        Name = "Премии за достижения",
                        Description = "Премии за выполнение планов и целей",
                        TypeId = 2,
                        Color = "#FFCC00",
                    },
                ],
            },
        };

        lastIndex = SetCategoryIds(categories, ref startIndex);
        return categories;
    }

    public static (List<Operation> operations, List<Place> places) SeedOperations(
        int userId, List<Category> categories, DateTime now, int startIndex = 0, int placeStartIndex = 0)
    {
        var places = SeedPlaces(userId, placeStartIndex);
        var allCategories = GetAllCategories(categories);
        var expenseCategories = allCategories.Where(c => c.TypeId == 1).ToList();
        var incomeCategories = allCategories.Where(c => c.TypeId == 2).ToList();

        var random = new Random(42);
        var operations = new List<Operation>();
        var operationId = startIndex;

        for (var monthOffset = -5; monthOffset <= 0; monthOffset++)
        {
            var monthDate = now.AddMonths(monthOffset);
            var daysInMonth = DateTime.DaysInMonth(monthDate.Year, monthDate.Month);

            for (var i = 0; i < 20; i++)
            {
                operationId++;
                var isIncome = random.NextDouble() < 0.25;
                var category = isIncome
                    ? incomeCategories[random.Next(incomeCategories.Count)]
                    : expenseCategories[random.Next(expenseCategories.Count)];

                var day = random.Next(1, daysInMonth + 1);
                var sum = isIncome
                    ? Math.Round((decimal)(random.NextDouble() * 100000 + 10000), 2)
                    : Math.Round((decimal)(random.NextDouble() * 5000 + 50), 2);

                var placeId = random.NextDouble() < 0.6
                    ? places[random.Next(places.Count)].Id
                    : (int?)null;

                operations.Add(new Operation
                {
                    UserId = userId,
                    Id = operationId,
                    Sum = sum,
                    CategoryId = category.Id,
                    Comment = $"{category.Name} — {new DateOnly(monthDate.Year, monthDate.Month, day):MMMM yyyy}",
                    Date = new DateTime(monthDate.Year, monthDate.Month, day, 0, 0, 0, DateTimeKind.Utc),
                    PlaceId = placeId,
                });
            }
        }

        return (operations, places);
    }

    public static List<FastOperation> SeedFastOperations(int userId, List<Category> categories, int startIndex = 0)
    {
        var allCategories = GetAllCategories(categories);
        var byName = allCategories.ToDictionary(c => c.Name, c => c.Id);

        return
        [
            new FastOperation
            {
                UserId = userId, Id = startIndex + 1,
                Name = "Кофе", Sum = 150m, CategoryId = byName["Продукты"], Order = 1,
            },
            new FastOperation
            {
                UserId = userId, Id = startIndex + 2,
                Name = "Обед", Sum = 500m, CategoryId = byName["Продукты"], Order = 2,
            },
            new FastOperation
            {
                UserId = userId, Id = startIndex + 3,
                Name = "Метро", Sum = 60m, CategoryId = byName["Общественный транспорт"], Order = 3,
            },
            new FastOperation
            {
                UserId = userId, Id = startIndex + 4,
                Name = "Такси", Sum = 400m, CategoryId = byName["Такси"], Order = 4,
            },
            new FastOperation
            {
                UserId = userId, Id = startIndex + 5,
                Name = "Продукты", Sum = 2000m, CategoryId = byName["Продукты"], Order = 5,
            },
        ];
    }

    public static List<RegularOperation> SeedRegularOperations(
        int userId, List<Category> categories, DateTime now, int startIndex = 0)
    {
        var allCategories = GetAllCategories(categories);
        var byName = allCategories.ToDictionary(c => c.Name, c => c.Id);
        var sixMonthsAgo = now.AddMonths(-6).Date;

        return
        [
            new RegularOperation
            {
                UserId = userId, Id = startIndex + 1,
                Name = "Аренда квартиры", Sum = 30000m,
                CategoryId = byName["Коммунальные услуги"],
                TimeTypeId = 1, TimeValue = 1,
                DateFrom = sixMonthsAgo,
            },
            new RegularOperation
            {
                UserId = userId, Id = startIndex + 2,
                Name = "Подписка на стриминг", Sum = 700m,
                CategoryId = byName["Развлечения"],
                TimeTypeId = 1, TimeValue = 1,
                DateFrom = sixMonthsAgo,
            },
            new RegularOperation
            {
                UserId = userId, Id = startIndex + 3,
                Name = "Зарплата", Sum = 150000m,
                CategoryId = byName["Зарплата"],
                TimeTypeId = 1, TimeValue = 1,
                DateFrom = sixMonthsAgo,
            },
            new RegularOperation
            {
                UserId = userId, Id = startIndex + 4,
                Name = "Электричество", Sum = 3000m,
                CategoryId = byName["Электричество"],
                TimeTypeId = 1, TimeValue = 1,
                DateFrom = sixMonthsAgo,
            },
        ];
    }

    private static List<Place> SeedPlaces(int userId, int startIndex = 0)
    {
        var places = new List<Place>
        {
            new()
            {
                UserId = userId,
                Id = startIndex + 1,
                Name = "Работа",
                LastUsedDate = DateTime.UtcNow,
            },

            new()
            {
                UserId = userId,
                Id = startIndex + 2,
                Name = "Супермаркет",
                LastUsedDate = DateTime.UtcNow,
            },

            new()
            {
                UserId = userId,
                Id = startIndex + 3,
                Name = "Концертный зал",
                LastUsedDate = DateTime.UtcNow,
            },

            new()
            {
                UserId = userId,
                Id = startIndex + 4,
                Name = "Квартира",
                LastUsedDate = DateTime.UtcNow,
            },

            new()
            {
                UserId = userId,
                Id = startIndex + 5,
                Name = "Поликлиника",
                LastUsedDate = DateTime.UtcNow,
            },

            new()
            {
                UserId = userId,
                Id = startIndex + 6,
                Name = "Магазин одежды",
                LastUsedDate = DateTime.UtcNow,
            },

            new()
            {
                UserId = userId,
                Id = startIndex + 7,
                Name = "Кинотеатр",
                LastUsedDate = DateTime.UtcNow,
            },

            new()
            {
                UserId = userId,
                Id = startIndex + 8,
                Name = "Учебный центр",
                LastUsedDate = DateTime.UtcNow,
            },

            new()
            {
                UserId = userId,
                Id = startIndex + 9,
                Name = "Магазин продуктов",
                LastUsedDate = DateTime.UtcNow,
            },
        };

        return places;
    }

    public static List<DebtOwner> SeedDebtOwners(int userId, int startIndex = 0)
    {
        return
        [
            new DebtOwner { UserId = userId, Id = startIndex + 1, Name = "Иван Петров" },
            new DebtOwner { UserId = userId, Id = startIndex + 2, Name = "Мария Сидорова" },
            new DebtOwner { UserId = userId, Id = startIndex + 3, Name = "Алексей Козлов" },
        ];
    }

    public static List<Debt> SeedDebts(int userId, List<DebtOwner> owners, DateTime now, int startIndex = 0)
    {
        return
        [
            new Debt
            {
                UserId = userId, Id = startIndex + 1,
                OwnerId = owners[0].Id, TypeId = 1, Sum = 15000m, PaySum = 0m,
                StatusId = 1, Date = now.AddDays(-10),
                Comment = "Долг за ужин",
            },
            new Debt
            {
                UserId = userId, Id = startIndex + 2,
                OwnerId = owners[1].Id, TypeId = 2, Sum = 50000m, PaySum = 20000m,
                StatusId = 2, Date = now.AddDays(-45),
                Comment = "Заём на ремонт", PayComment = "Частичный возврат",
            },
            new Debt
            {
                UserId = userId, Id = startIndex + 3,
                OwnerId = owners[2].Id, TypeId = 1, Sum = 3000m, PaySum = 3000m,
                StatusId = 3, Date = now.AddDays(-80),
                Comment = "За билеты на концерт", PayComment = "Полностью погашен",
            },
            new Debt
            {
                UserId = userId, Id = startIndex + 4,
                OwnerId = owners[0].Id, TypeId = 2, Sum = 25000m, PaySum = 0m,
                StatusId = 1, Date = now.AddDays(-5),
                Comment = "Одолжил на отпуск",
            },
            new Debt
            {
                UserId = userId, Id = startIndex + 5,
                OwnerId = owners[1].Id, TypeId = 1, Sum = 8000m, PaySum = 4000m,
                StatusId = 2, Date = now.AddDays(-60),
                Comment = "За подарок", PayComment = "Половина",
            },
            new Debt
            {
                UserId = userId, Id = startIndex + 6,
                OwnerId = owners[2].Id, TypeId = 2, Sum = 100000m, PaySum = 0m,
                StatusId = 1, Date = now.AddDays(-20),
                Comment = "Заём на автомобиль",
            },
        ];
    }

    public static List<Car> SeedCars(int userId, int startIndex = 0)
    {
        return
        [
            new Car { UserId = userId, Id = startIndex + 1, Name = "Toyota Camry" },
            new Car { UserId = userId, Id = startIndex + 2, Name = "Volkswagen Golf" },
        ];
    }

    public static List<CarEvent> SeedCarEvents(int userId, List<Car> cars, DateTime now, int startIndex = 0)
    {
        var camryId = cars[0].Id;
        var golfId = cars[1].Id;

        return
        [
            new CarEvent
            {
                UserId = userId, Id = startIndex + 1, CarId = camryId,
                TypeId = 1, Title = "ТО-1 плановое", Comment = "Замена масла и фильтров",
                Mileage = 15000, Date = now.AddMonths(-5),
            },
            new CarEvent
            {
                UserId = userId, Id = startIndex + 2, CarId = camryId,
                TypeId = 2, Title = "Заправка", Comment = "АИ-95, полный бак",
                Mileage = 15800, Date = now.AddMonths(-4),
            },
            new CarEvent
            {
                UserId = userId, Id = startIndex + 3, CarId = camryId,
                TypeId = 3, Title = "Замена тормозных колодок",
                Mileage = 20000, Date = now.AddMonths(-2),
            },
            new CarEvent
            {
                UserId = userId, Id = startIndex + 4, CarId = camryId,
                TypeId = 4, Title = "ОСАГО", Comment = "Годовой полис",
                Mileage = 22000, Date = now.AddMonths(-1),
            },
            new CarEvent
            {
                UserId = userId, Id = startIndex + 5, CarId = golfId,
                TypeId = 1, Title = "ТО-2 плановое", Comment = "Полное ТО",
                Mileage = 45000, Date = now.AddMonths(-4),
            },
            new CarEvent
            {
                UserId = userId, Id = startIndex + 6, CarId = golfId,
                TypeId = 2, Title = "Заправка", Comment = "АИ-92",
                Mileage = 46200, Date = now.AddMonths(-3),
            },
            new CarEvent
            {
                UserId = userId, Id = startIndex + 7, CarId = golfId,
                TypeId = 3, Title = "Ремонт подвески", Comment = "Замена стоек стабилизатора",
                Mileage = 48000, Date = now.AddMonths(-1),
            },
            new CarEvent
            {
                UserId = userId, Id = startIndex + 8, CarId = golfId,
                TypeId = 2, Title = "Заправка", Comment = "АИ-92, 40 литров",
                Mileage = 49500, Date = now.AddDays(-7),
            },
        ];
    }

    private static int SetCategoryIds(List<Category> categories, ref int currentIndex)
    {
        foreach (var category in categories)
        {
            category.Id = currentIndex++;

            if (category.SubCategories is { Count: > 0 })
            {
                SetCategoryIds(category.SubCategories, ref currentIndex);
            }
        }

        return currentIndex;
    }

    private static List<Category> GetAllCategories(List<Category> categories)
    {
        var allCategories = new List<Category>();
        GetAllCategoriesRecursive(categories, allCategories);
        return allCategories;
    }

    private static void GetAllCategoriesRecursive(List<Category> categories, List<Category> allCategories)
    {
        foreach (var category in categories)
        {
            allCategories.Add(category);

            if (category.SubCategories is { Count: > 0 })
            {
                GetAllCategoriesRecursive(category.SubCategories, allCategories);
            }
        }
    }
}
