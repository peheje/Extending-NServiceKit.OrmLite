﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using mySql;
using NServiceKit.DataAnnotations;
using NServiceKit.OrmLite;
using NServiceKit.OrmLite.MySql;
using NUnit.Framework;
using OrmLitePehjExtensions;
using OrmLitePehjExtensions.System.Data;

namespace mySqlTests
{
	[TestFixture]
	class DbConnectionExtensionsTest
	{
		//This is hard to test, so therefore there is a some dependencies, which I just cannot avoid

		//First up is a path to a readable file that contains your connection string to your database
		private static string connectionString_;

		//Next up is a ISqlProvider which the whole ordeal is dependent of. If MySql or MsSql syntax changes, these tests will fail, change this to your DB type. You may have to implement your own
		public static ISqlProvider CustomProvider = new MySqlProvider();
		public static IOrmLiteDialectProvider OrmLiteDialectProvider = MySqlDialectProvider.Instance;

		class SmallTestClass
		{
			[AutoIncrement]
			public int Id { get; set; }

			public DateTime Time { get; set; }
			public string Name { get; set; }
			public decimal Amount { get; set; }
			//On DB these exists:
			//public double Precision { get; set; }
			//public DateTime TargetDate { get; set; }
		}

		class BigTestClass
		{
			[AutoIncrement]
			public int Id { get; set; }

			public DateTime Time { get; set; }
			public string Name { get; set; }
			public decimal Amount { get; set; }
			//On DB these does not exist:
			public int Code { get; set; }
			public DateTime? NullAbleDateTime { get; set; }
		}

		class TestClassWithFk
		{
			[AutoIncrement]
			public int Id { get; set; }

			//DB this exists:
			//[ForeignKey(typeof(SmallTestClass), OnDelete = "NO ACTION", OnUpdate = "NO ACTION")]
			//public int SmallTestClassId { get; set; }
		}

		class TestClassWithoutFk
		{
			[AutoIncrement]
			public int Id { get; set; }

			//On DB this does not exists:
			[ForeignKey(typeof(SmallTestClass), OnDelete = "NO ACTION", OnUpdate = "NO ACTION")]
			public int SmallTestClassId { get; set; }
		}

		//Model isn't generated in setup
		class DoesntExist
		{
			[AutoIncrement]
			public int Id { get; set; }
			public string Test { get; set; }
		}

		[SetUp]
		public void Init()
		{
			const string connectionStringPath = @"\\psf\Dropbox\Dox\Mac-Git\connectionString.txt";
			connectionString_ = ConnectionStringReader.GetConnectionString(connectionStringPath);
			var dbFactory = new OrmLiteConnectionFactory(connectionString_, OrmLiteDialectProvider);
			using (var db = dbFactory.OpenDbConnection())
			{
				db.ExecuteSql(CustomProvider.CreateSmallTestClass());
				db.ExecuteSql(CustomProvider.CreateBigTestClass());
				db.ExecuteSql(CustomProvider.CreateTestClassWithFk());
				db.ExecuteSql(CustomProvider.CreateTestClassWithOutFk());
			}
		}

		[TearDown]
		public void Dispose()
		{
			var dbFactory = new OrmLiteConnectionFactory(connectionString_, OrmLiteDialectProvider);
			using (var db = dbFactory.OpenDbConnection())
			{
				db.ExecuteSql(CustomProvider.DropTestClasses());
			}
		}

		[Test]
		public static void SetupTest()
		{
			var dbFactory = new OrmLiteConnectionFactory(connectionString_, OrmLiteDialectProvider);
			using (var db = dbFactory.OpenDbConnection())
			{
				Assert.DoesNotThrow(() =>
				{
					db.CreateTable<SmallTestClass>();
					db.Insert(new SmallTestClass
					{
						Name = "Test",
						Time = new DateTime(1990, 1, 1)
					});
					var test = db.Select<SmallTestClass>().FirstOrDefault();
					Assert.NotNull(test);
					test.Name = "TestName";
					test.Time = new DateTime(2000, 12, 12);
				});				
			}
		}

		[Test]
		public static void UpdateTable_DbTableHasFewerColumnsThanModelInsertingThrowsSqlException_AddExtraColumnsOnDbSoWeCanInsertNewDoesNotThrowException()
		{
			var dbFactory = new OrmLiteConnectionFactory(connectionString_, OrmLiteDialectProvider);
			using (var db = dbFactory.OpenDbConnection())
			{
				//Arrange by asserting that this throws:
				Assert.That(() => db.Insert(new BigTestClass
				{
					Amount = 100m,
					Code = 1,
					Name = "Test",
					NullAbleDateTime = new DateTime(2000, 1, 1),
					Time = DateTime.Now
				}), Throws.Exception);

				//Act which fixes it
				db.UpdateTable<BigTestClass>(CustomProvider, true);

				//Assert it does not throw
				Assert.DoesNotThrow(() => db.Insert(new BigTestClass
				{
					Amount = 100m,
					Code = 1,
					Name = "Test",
					NullAbleDateTime = new DateTime(2000, 1, 1),
					Time = DateTime.Now
				}));
			}
		}

		[Test]
		public static void UpdateTable_DbTableHasMoreColumnsThanModel_RemoveDbColumnsNotInModel()
		{
			var dbFactory = new OrmLiteConnectionFactory(connectionString_, OrmLiteDialectProvider);
			using (var db = dbFactory.OpenDbConnection())
			{
				//Arrange by asserting that these exists on db, after function they should not exist anymore
				var smallTestClass = CustomProvider.SpecialQuotes + "SmallTestClass" + CustomProvider.SpecialQuotes;
				var prevision = CustomProvider.SpecialQuotes + "Precision" + CustomProvider.SpecialQuotes;
				var targetDate = CustomProvider.SpecialQuotes + "TargetDate" + CustomProvider.SpecialQuotes;

				//Executes raw sql to server
				var countPrecision =
						db.SqlList<string>(CustomProvider.CheckIfColumnExists(smallTestClass, prevision)).Count;
				Assert.GreaterOrEqual(countPrecision, 1);

				var targetDateCount =
						db.SqlList<string>(CustomProvider.CheckIfColumnExists(smallTestClass, targetDate)).Count;
				Assert.GreaterOrEqual(targetDateCount, 1);

				//Act
				db.UpdateTable<SmallTestClass>(CustomProvider, true);

				//Assert that they are gone
				var countPrecisionAfter =
						db.SqlList<string>(CustomProvider.CheckIfColumnExists(smallTestClass, prevision)).Count;
				Assert.LessOrEqual(countPrecisionAfter, 0);

				var targetDateCountAfter =
						db.SqlList<string>(CustomProvider.CheckIfColumnExists(smallTestClass, targetDate)).Count;
				Assert.LessOrEqual(targetDateCountAfter, 0);
			}
		}

		[Test]
		public static void UpdateTable_DbTableHasColumnWithForeignKeyConstraintButModelDoesNotHaveColumn_DeleteFkConstraintThenDeleteDbColumn()
		{
			var dbFactory = new OrmLiteConnectionFactory(connectionString_, OrmLiteDialectProvider);
			using (var db = dbFactory.OpenDbConnection())
			{
				//Arrange by asserting that fk column on DB exists, first surround the tabel names and columns with quotes
				var testClassWithFk = CustomProvider.SpecialQuotes + "TestClassWithFk" + CustomProvider.SpecialQuotes;
				var fkName = CustomProvider.SpecialQuotes + "SmallTestClassId" + CustomProvider.SpecialQuotes;
					
				var hasFkConstraint =
					db.SqlList<string>(CustomProvider.GetForeignKeyConstraintName(testClassWithFk, fkName));
				Assert.GreaterOrEqual(hasFkConstraint.Count, 1);

				//Act
				db.UpdateTable<TestClassWithFk>(CustomProvider, true);

				//Assert that the constaint and column is gone
				var hasFkConstraintAfter =
					db.SqlList<string>(CustomProvider.GetForeignKeyConstraintName(testClassWithFk, fkName));
				Assert.LessOrEqual(hasFkConstraintAfter.Count, 0);

				var columnCount = db.SqlList<string>(CustomProvider.CheckIfColumnExists(testClassWithFk, fkName)).Count;
				Assert.LessOrEqual(columnCount, 0);
			}
		}

		[Test]
		public static void UpdateTable_ModelHasPropertyThatIsForeignKeyButDbDoesNot_DatabaseGetsUpdatedToHaveColumnWithFkConstraint()
		{
			var dbFactory = new OrmLiteConnectionFactory(connectionString_, OrmLiteDialectProvider);
			using (var db = dbFactory.OpenDbConnection())
			{
				//Arrange by asserting that fk column on DB does not exists
				var tableWithoutFk = CustomProvider.SpecialQuotes + "TestClassWithoutFk" + CustomProvider.SpecialQuotes;
				var fkName = CustomProvider.SpecialQuotes + "SmallTestClassId" + CustomProvider.SpecialQuotes;

				//Assert that column does not exist
				var noFkColumn = db.SqlList<string>(CustomProvider.CheckIfColumnExists(tableWithoutFk, fkName)).Count;
				Assert.LessOrEqual(noFkColumn, 0);

				//Assert that constraint does not exist (redundant I guess)
				//Check that fk constraint exists
				var constraint = db.SqlList<string>(CustomProvider.GetForeignKeyConstraintName(tableWithoutFk, fkName)).Count;
				Assert.LessOrEqual(constraint, 0);

				//Act
				db.UpdateTable<TestClassWithoutFk>(CustomProvider, true);

				//Check that column exists
				var fkColumn = db.SqlList<string>(CustomProvider.CheckIfColumnExists(tableWithoutFk, fkName)).Count;
				Assert.GreaterOrEqual(fkColumn, 1);

				//Check that fk constraint exists
				var constraintAfter = db.SqlList<string>(CustomProvider.GetForeignKeyConstraintName(tableWithoutFk, fkName)).Count;
				Assert.GreaterOrEqual(constraintAfter, 1);

			}
		}

		[Test]
		public static void UpdateTable_TableDoesntExistOnDb_ItGetsCreatedOnDb()
		{
			var dbFactory = new OrmLiteConnectionFactory(connectionString_, OrmLiteDialectProvider);
			using (var db = dbFactory.OpenDbConnection())
			{
				//Arrange by asserting it doesn't exist
				var table = CustomProvider.SpecialQuotes + "DoesntExist" + CustomProvider.SpecialQuotes;
				var testProperty = CustomProvider.SpecialQuotes + "Test" + CustomProvider.SpecialQuotes;

				//Assert column test doens't exists
				var noTestColumn = db.SqlList<string>(CustomProvider.CheckIfColumnExists(table, testProperty)).Count;
				Assert.LessOrEqual(noTestColumn, 0);

				//Act
				db.UpdateTable<DoesntExist>(CustomProvider, true);

				//Assert test column exists
				var testColumn = db.SqlList<string>(CustomProvider.CheckIfColumnExists(table, testProperty)).Count;
				Assert.GreaterOrEqual(testColumn, 1);

				//Cleanup
				db.DropTable<DoesntExist>();
			}
		}

	}
}
