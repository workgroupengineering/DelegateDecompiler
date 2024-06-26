﻿// Contributed by @JonPSmith (GitHub) www.thereformedprogrammer.com

using System.Linq;
using DelegateDecompiler.EntityFramework.Tests.Helpers;
using NUnit.Framework;

namespace DelegateDecompiler.EntityFramework.Tests.TestGroup50Types
{
    class Test01Strings
    {
        private ClassEnvironment classEnv;

        [OneTimeSetUp]
        public void SetUpFixture()
        {
            classEnv = new ClassEnvironment();
        }

        [Test]
        public void TestConcatenatePersonNotHandleNull()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfPersons.Select(x => x.FirstName + " " + x.MiddleName + " " + x.LastName).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfPersons.Select(x => x.FullNameNoNull)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestConcatenatePersonHandleNull()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfPersons.Select(x => x.FirstName + (x.MiddleName == null ? "" : " ") + x.MiddleName + " " + x.LastName).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfPersons.Select(x => x.FullNameHandleNull)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestConcatenatePersonHandleNameOrder()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfPersons.Select(x => x.NameOrder
                    ? x.LastName + ", " + x.FirstName + (x.MiddleName == null ? "" : " ")
                    : x.FirstName + (x.MiddleName == null ? "" : " ") + x.MiddleName + " " + x.LastName).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfPersons.Select(x => x.UseOrderToFormatNameStyle)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestSelectGenericMethodPersonHandle()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfPersons.Select(x => x.FirstName + (x.MiddleName == null ? "" : " ") + x.MiddleName + " " + x.LastName).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = GetGenericPersonHandle(env.Db.EfPersons)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestFilterGenericMethodPersonHandle()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                var linq = env.Db.EfPersons.Where(x => x.FirstName + (x.MiddleName == null ? "" : " ") + x.MiddleName + " " + x.LastName != null).ToList();

                env.AboutToUseDelegateDecompiler();
                var dd = FilterGenericPersonHandle(env.Db.EfPersons)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                env.CompareAndLogList(linq, dd);
            }
        }

        public IQueryable<string> GetGenericPersonHandle<T>(IQueryable<T> people)
            where T : class, EfItems.Abstracts.IPerson
        {
            return people.Select(x => x.FullNameHandleNull);
        }

        static IQueryable<T> FilterGenericPersonHandle<T>(IQueryable<T> people)
            where T : EfItems.Abstracts.IPerson
        {
            return people.Where(x => x.FullNameHandleNull != null);
        }
    }
}
