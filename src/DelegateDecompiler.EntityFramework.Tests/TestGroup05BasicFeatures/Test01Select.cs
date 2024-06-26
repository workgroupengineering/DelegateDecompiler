﻿// Contributed by @JonPSmith (GitHub) www.thereformedprogrammer.com

using System;
using System.Linq;
using DelegateDecompiler.EntityFramework.Tests.EfItems.Abstracts;
using DelegateDecompiler.EntityFramework.Tests.Helpers;
using NUnit.Framework;

namespace DelegateDecompiler.EntityFramework.Tests.TestGroup05BasicFeatures
{
    class Test01Select
    {
        private ClassEnvironment classEnv;

        [OneTimeSetUp]
        public void SetUpFixture()
        {
            classEnv = new ClassEnvironment();
        }

        [Test]
        public void TestBoolEqualsConstant()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfParents.Select(x => x.ParentBool == true).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfParents.Select(x => x.BoolEqualsConstant)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        private static bool staticBool = true;

        [Test]
        public void TestBoolEqualsStaticVariable()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfParents.Select(x => x.ParentBool == staticBool).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfParents.Select(x => x.BoolEqualsStaticVariable)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestIntEqualsConstant()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfParents.Select(x => x.ParentInt == 123).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfParents.Select(x => x.IntEqualsConstant)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestSelectPropertyWithoutComputedAttribute()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfPersons.Select(x => x.FirstName + " " + x.MiddleName + " " + x.LastName).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfPersons.Select(x => x.FullNameNoAttibute)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestSelectMethodWithoutComputedAttribute()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.EfPersons.Select(x => x.FirstName + " " + x.MiddleName + " " + x.LastName).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfPersons.Select(x => x.GetFullNameNoAttibute())
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestSelectAbstractMemberOverTphHierarchy()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.LivingBeeing.ToList().Select(l => l.Species).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.LivingBeeing.Select(p => p.Species)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestSelectAbstractMemberOverTphHierarchyAfterRestrictingToSubtype()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.LivingBeeing.OfType<Animal>().ToList().Select(p => p.Species).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.LivingBeeing.OfType<Animal>().Select(p => p.Species)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

#if EF_CORE
        [Test]
        public void TestSelectAbstractMemberOverTphHierarchyWithGenericClassesAfterRestrictingToSubtype()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.LivingBeeing.OfType<Fish>().ToList().Select(p => new { p.Species, p.Group }).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.LivingBeeing.OfType<Fish>().Select(p => new { p.Species, p.Group }).ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestSelectAbstractMemberWithConditionOnItOverTphHierarchyWithGenericClassesAfterRestrictingToSubtype()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.LivingBeeing.OfType<Fish>().ToList()
                    .Select(p => new { p.Species, p.Group })
                    .Where(p => p.Species != null && p.Group != null)
                    .ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd1 = env.Db.LivingBeeing.OfType<Fish>()
                    .Select(p => new { p.Species, p.Group })
                    .Where(p => p.Species != null && p.Group != null)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    ;

                var dd = dd1.ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }
#endif

        [Test]
        public void TestSelectMultipleLevelsOfAbstractMembersOverTphHierarchy()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var linq = env.Db.LivingBeeing.OfType<Animal>().ToList().Select(p => string.Concat(p.Species, " : ", p.IsPet)).ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.LivingBeeing.OfType<Animal>().Select(p => string.Concat(p.Species, " : ", p.IsPet))
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }

        [Test]
        public void TestSelectSelectMany()
        {
            using (var env = new MethodEnvironment(classEnv))
            {
                //SETUP
                var children = env.Db.EfParents.Select(x => x.Children.SelectMany(c => c.GrandChildren).OrderBy(c => c.EfGrandChildId).FirstOrDefault());
                var linq = children.ToList();

                //ATTEMPT
                env.AboutToUseDelegateDecompiler();
                var dd = env.Db.EfParents.Select(x => x.FirstGrandChild)
#if NO_AUTO_DECOMPILE
                    .Decompile()
#endif
                    .ToList();

                //VERIFY
                env.CompareAndLogList(linq, dd);
            }
        }
    }
}
