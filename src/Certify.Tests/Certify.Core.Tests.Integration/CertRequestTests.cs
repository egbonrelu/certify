﻿using Certify.Management;
using Certify.Management.Servers;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Core.Tests
{
    [TestClass]
    /// <summary>
    /// Integration tests for CertifyManager 
    /// </summary>
    public class CertRequestTests : IntegrationTestBase, IDisposable
    {
        private ServerProviderIIS iisManager;
        private CertifyManager certifyManager;
        private string testSiteName = "Test1CertRequest";
        private string testSiteDomain = "";
        private string testSitePath = "c:\\inetpub\\wwwroot";
        private int testSiteHttpPort = 81;

        public CertRequestTests()
        {
            certifyManager = new CertifyManager();
            iisManager = new ServerProviderIIS();

            // see integrationtestbase for environment variable replacement
            testSiteDomain = "integration1." + PrimaryTestDomain;
            testSitePath = PrimaryIISRoot;

            //perform setup for IIS
            SetupIIS();
        }

        /// <summary>
        /// Perform teardown for IIS 
        /// </summary>
        public void Dispose()
        {
            TeardownIIS();
        }

        public void SetupIIS()
        {
            if (iisManager.SiteExists(testSiteName))
            {
                iisManager.DeleteSite(testSiteName);
            }

            iisManager.CreateSite(testSiteName, testSiteDomain, PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);
            Assert.IsTrue(iisManager.SiteExists(testSiteName));
        }

        public void TeardownIIS()
        {
            iisManager.DeleteSite(testSiteName);
            Assert.IsFalse(iisManager.SiteExists(testSiteName));
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestHttp01()
        {
            var site = iisManager.GetSiteByDomain(testSiteDomain);
            Assert.AreEqual(site.Name, testSiteName);

            var dummyManagedSite = new ManagedSite
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = testSiteDomain,
                    ChallengeType = "http-01",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testSitePath
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS
            };

            var result = await certifyManager.PerformCertificateRequest(dummyManagedSite);

            //ensure cert request was successful
            Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

            //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
            var managedSites = await certifyManager.GetManagedSites();
            var managedSite = managedSites.FirstOrDefault(m => m.Id == dummyManagedSite.Id);

            //emsure we have a new managed site
            Assert.IsNotNull(managedSite);

            //have cert file details
            Assert.IsNotNull(managedSite.CertificatePath);

            var fileExists = System.IO.File.Exists(managedSite.CertificatePath);
            Assert.IsTrue(fileExists);

            //check cert is correct
            var certInfo = CertificateManager.LoadCertificate(managedSite.CertificatePath);
            Assert.IsNotNull(certInfo);

            bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
            Assert.IsTrue(isRecentlyCreated);

            bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
            Assert.IsTrue(expiresInFuture);

            // remove managed site
            await certifyManager.DeleteManagedSite(managedSite.Id);
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestDNS()
        {
            var site = iisManager.GetSiteByDomain(testSiteDomain);
            Assert.AreEqual(site.Name, testSiteName);

            var dummyManagedSite = new ManagedSite
            {
                Id = Guid.NewGuid().ToString(),
                Name = testSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = testSiteDomain,
                    ChallengeType = "dns-01",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testSitePath,
                    ChallengeProvider = "DNS01.API.Route53",
                    ChallengeCredentialKey = "fbbafa6f-eafd-4d92-9986-72bd8067f372" //TODO: make configurable
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS
            };

            var result = await certifyManager.PerformCertificateRequest(dummyManagedSite);

            //ensure cert request was successful
            Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

            //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
            var managedSites = await certifyManager.GetManagedSites();
            var managedSite = managedSites.FirstOrDefault(m => m.Id == dummyManagedSite.Id);

            //emsure we have a new managed site
            Assert.IsNotNull(managedSite);

            //have cert file details
            Assert.IsNotNull(managedSite.CertificatePath);

            var fileExists = System.IO.File.Exists(managedSite.CertificatePath);
            Assert.IsTrue(fileExists);

            //check cert is correct
            var certInfo = CertificateManager.LoadCertificate(managedSite.CertificatePath);
            Assert.IsNotNull(certInfo);

            bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
            Assert.IsTrue(isRecentlyCreated);

            bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
            Assert.IsTrue(expiresInFuture);

            // remove managed site
            await certifyManager.DeleteManagedSite(managedSite.Id);

            // cleanup certificate
            CertificateManager.RemoveCertificate(certInfo);
        }

        [TestMethod, TestCategory("MegaTest")]
        public async Task TestChallengeRequestDNSWildcard()
        {
            var testStr = Guid.NewGuid().ToString().Substring(0, 6);
            PrimaryTestDomain = $"test-{testStr}." + PrimaryTestDomain;
            var wildcardDomain = "*.test." + PrimaryTestDomain;
            string testWildcardSiteName = "TestWildcard_" + testStr;

            if (iisManager.SiteExists(testWildcardSiteName))
            {
                iisManager.DeleteSite(testWildcardSiteName);
            }

            iisManager.CreateSite(testWildcardSiteName, "test" + testStr + "." + PrimaryTestDomain, PrimaryIISRoot, "DefaultAppPool", port: testSiteHttpPort);
            var site = iisManager.GetSiteByDomain("test" + testStr + "." + PrimaryTestDomain);

            var dummyManagedSite = new ManagedSite
            {
                Id = Guid.NewGuid().ToString(),
                Name = testWildcardSiteName,
                GroupId = site.Id.ToString(),
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = wildcardDomain,
                    ChallengeType = "dns-01",
                    PerformAutoConfig = true,
                    PerformAutomatedCertBinding = true,
                    PerformChallengeFileCopy = true,
                    PerformExtensionlessConfigChecks = true,
                    WebsiteRootPath = testSitePath,
                    ChallengeProvider = "DNS01.API.Route53",
                    ChallengeCredentialKey = "fbbafa6f-eafd-4d92-9986-72bd8067f372" //TODO: make configurable
                },
                ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS
            };

            var result = await certifyManager.PerformCertificateRequest(dummyManagedSite);

            //ensure cert request was successful
            Assert.IsTrue(result.IsSuccess, "Certificate Request Not Completed");

            //check details of cert, subject alternative name should include domain and expiry must be great than 89 days in the future
            var managedSites = await certifyManager.GetManagedSites();
            var managedSite = managedSites.FirstOrDefault(m => m.Id == dummyManagedSite.Id);

            //emsure we have a new managed site
            Assert.IsNotNull(managedSite);

            //have cert file details
            Assert.IsNotNull(managedSite.CertificatePath);

            var fileExists = System.IO.File.Exists(managedSite.CertificatePath);
            Assert.IsTrue(fileExists);

            //check cert is correct
            var certInfo = CertificateManager.LoadCertificate(managedSite.CertificatePath);
            Assert.IsNotNull(certInfo);

            bool isRecentlyCreated = Math.Abs((DateTime.UtcNow - certInfo.NotBefore).TotalDays) < 2;
            Assert.IsTrue(isRecentlyCreated);

            bool expiresInFuture = (certInfo.NotAfter - DateTime.UtcNow).TotalDays >= 89;
            Assert.IsTrue(expiresInFuture);

            // remove managed site
            await certifyManager.DeleteManagedSite(managedSite.Id);

            // remove IIS site
            iisManager.DeleteSite(testWildcardSiteName);

            // cleanup certificate
            CertificateManager.RemoveCertificate(certInfo);
        }
    }
}