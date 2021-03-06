﻿//-----------------------------------------------------------------------
// <copyright file="GetKeyDeliveryUrlTests.cs" company="Microsoft">Copyright 2014 Microsoft Corporation</copyright>
// <license>
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </license>

using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.IdentityModel.Tokens;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Practices.TransientFaultHandling;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.MediaServices.Client.ContentKeyAuthorization;
using Microsoft.WindowsAzure.MediaServices.Client.Tests.Common;
using Microsoft.WindowsAzure.MediaServices.Client.Tests.DynamicEncryption;

namespace Microsoft.WindowsAzure.MediaServices.Client.Tests
{
    [TestClass]
    public class GetKeyDeliveryUrlTests
    {
        private CloudMediaContext _mediaContext;

        [TestInitialize]
        public void SetupTest()
        {
            _mediaContext = WindowsAzureMediaServicesTestConfiguration.CreateCloudMediaContext();
        }

        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [TestCategory("Bvt")]
        public void GetPlayReadyLicenseDeliveryUrl()
        {
            IContentKey contentKey = null;
            IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy = null;
            IContentKeyAuthorizationPolicyOption policyOption = null;

            try
            {
                contentKey = CreateTestKey(_mediaContext, ContentKeyType.CommonEncryption);

                PlayReadyLicenseResponseTemplate responseTemplate = new PlayReadyLicenseResponseTemplate();
                responseTemplate.LicenseTemplates.Add(new PlayReadyLicenseTemplate());
                string licenseTemplate = MediaServicesLicenseTemplateSerializer.Serialize(responseTemplate);

                policyOption = ContentKeyAuthorizationPolicyOptionTests.CreateOption(_mediaContext, String.Empty, ContentKeyDeliveryType.PlayReadyLicense, null, licenseTemplate, ContentKeyRestrictionType.Open);

                List<IContentKeyAuthorizationPolicyOption> options = new List<IContentKeyAuthorizationPolicyOption>
                {
                    policyOption
                };

                contentKeyAuthorizationPolicy = CreateTestPolicy(_mediaContext, String.Empty, options, ref contentKey);

                Uri keyDeliveryServiceUri = contentKey.GetKeyDeliveryUrl(ContentKeyDeliveryType.PlayReadyLicense);

                Assert.IsNotNull(keyDeliveryServiceUri);
                Assert.IsTrue(0 == String.Compare(keyDeliveryServiceUri.AbsolutePath, "/PlayReady/", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                CleanupKeyAndPolicy(contentKey, contentKeyAuthorizationPolicy, policyOption);
            }
        }

        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [TestCategory("Bvt")]
        public void GetHlsKeyDeliveryUrlAndFetchKey()
        {
            IContentKey contentKey = null;
            IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy = null;
            IContentKeyAuthorizationPolicyOption policyOption = null;

            try
            {
                byte[] expectedKey = null;
                contentKey = CreateTestKey(_mediaContext, ContentKeyType.EnvelopeEncryption, out expectedKey);

                policyOption = ContentKeyAuthorizationPolicyOptionTests.CreateOption(_mediaContext, String.Empty, ContentKeyDeliveryType.BaselineHttp, null, null, ContentKeyRestrictionType.Open);

                List<IContentKeyAuthorizationPolicyOption> options = new List<IContentKeyAuthorizationPolicyOption>
                {
                    policyOption
                };

                contentKeyAuthorizationPolicy = CreateTestPolicy(_mediaContext, String.Empty, options, ref contentKey);

                Uri keyDeliveryServiceUri = contentKey.GetKeyDeliveryUrl(ContentKeyDeliveryType.BaselineHttp);

                Assert.IsNotNull(keyDeliveryServiceUri);

                // Enable once all accounts are enabled for per customer Key Delivery Urls
                //Assert.IsTrue(keyDeliveryServiceUri.Host.StartsWith(_mediaContext.Credentials.ClientId));

                KeyDeliveryServiceClient keyClient = new KeyDeliveryServiceClient(RetryPolicy.DefaultFixed);
                string rawkey = EncryptionUtils.GetKeyIdAsGuid(contentKey.Id).ToString();
                byte[] key = keyClient.AcquireHlsKeyWithBearerHeader(keyDeliveryServiceUri, TokenServiceClient.GetAuthTokenForKey(rawkey));

                string expectedString = GetString(expectedKey);
                string fetchedString = GetString(key);
                Assert.AreEqual(expectedString, fetchedString);
            }
            finally
            {
                CleanupKeyAndPolicy(contentKey, contentKeyAuthorizationPolicy, policyOption);
            }
        }

        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [TestCategory("Bvt")]
        [DeploymentItem("amscer.pfx") ]
        public void GetHlsKeyDeliveryUrlAndFetchKeyWithJWTAuthenticationWhenIssuerIsURI()
        {

            string audience = "http://sampleAudience";
            string issuer = "http://sampleIssuerUrl";
            FetchKeyWithJWTAuth(audience, issuer);
        }

        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [TestCategory("Bvt")]
        [DeploymentItem("amscer.pfx")]
        public void GetHlsKeyDeliveryUrlAndFetchKeyWithJWTAuthenticationWhenIssuerIsStringGuid()
        {

            string audience = Guid.NewGuid().ToString();
            string issuer = Guid.NewGuid().ToString();
            FetchKeyWithJWTAuth(audience, issuer);
        }

        private void FetchKeyWithJWTAuth(string audience, string issuer)
        {
            IContentKey contentKey = null;
            IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy = null;
            IContentKeyAuthorizationPolicyOption policyOption = null;

            try
            {
                byte[] expectedKey = null;
                contentKey = CreateTestKey(_mediaContext, ContentKeyType.EnvelopeEncryption, out expectedKey);

                var templatex509Certificate2 = new X509Certificate2("amscer.pfx", "AMSGIT");
                SigningCredentials cred = new X509SigningCredentials(templatex509Certificate2);

                TokenRestrictionTemplate tokenRestrictionTemplate = new TokenRestrictionTemplate(TokenType.JWT);
                tokenRestrictionTemplate.PrimaryVerificationKey = new X509CertTokenVerificationKey(templatex509Certificate2);
                tokenRestrictionTemplate.Audience = audience;
                tokenRestrictionTemplate.Issuer = issuer;

                string optionName = "GetHlsKeyDeliveryUrlAndFetchKeyWithJWTAuthentication";
                string requirements = TokenRestrictionTemplateSerializer.Serialize(tokenRestrictionTemplate);
                policyOption = ContentKeyAuthorizationPolicyOptionTests.CreateOption(_mediaContext, optionName,
                    ContentKeyDeliveryType.BaselineHttp, requirements, null, ContentKeyRestrictionType.TokenRestricted);

                JwtSecurityToken token = new JwtSecurityToken(issuer: tokenRestrictionTemplate.Issuer,
                    audience: tokenRestrictionTemplate.Audience, notBefore: DateTime.Now.AddMinutes(-5),
                    expires: DateTime.Now.AddMinutes(5), signingCredentials: cred);

                JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
                string jwtTokenString = handler.WriteToken(token);

                List<IContentKeyAuthorizationPolicyOption> options = new List<IContentKeyAuthorizationPolicyOption>
                {
                    policyOption
                };

                contentKeyAuthorizationPolicy = CreateTestPolicy(_mediaContext, String.Empty, options, ref contentKey);

                Uri keyDeliveryServiceUri = contentKey.GetKeyDeliveryUrl(ContentKeyDeliveryType.BaselineHttp);

                Assert.IsNotNull(keyDeliveryServiceUri);

                // Enable once all accounts are enabled for per customer Key Delivery Urls
                //Assert.IsTrue(keyDeliveryServiceUri.Host.StartsWith(_mediaContext.Credentials.ClientId));

                KeyDeliveryServiceClient keyClient = new KeyDeliveryServiceClient(RetryPolicy.DefaultFixed);
                byte[] key = keyClient.AcquireHlsKeyWithBearerHeader(keyDeliveryServiceUri, jwtTokenString);

                string expectedString = GetString(expectedKey);
                string fetchedString = GetString(key);
                Assert.AreEqual(expectedString, fetchedString);
            }
            finally
            {
                CleanupKeyAndPolicy(contentKey, contentKeyAuthorizationPolicy, policyOption);
            }
        }

        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [TestCategory("Bvt")]
        public void GetHlsKeyDeliveryUrlAndFetchKeyWithSWTAuthenticationWhenIssuerIsUri()
        {

            string audience = "http://sampleAudience";
            string issuer = "http://sampleIssuerUrl";
            FetchKeyWithSWTToken(audience, issuer);
        }

        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [TestCategory("Bvt")]
        [ExpectedException(typeof(InvalidDataContractException))]
        public void GetHlsKeyDeliveryUrlAndFetchKeyWithSWTAuthenticationWhenIssuerIsStringGuidShouldThrow()
        {

            string audience = "http://www.microsoft.com";
            string issuer = Guid.NewGuid().ToString();
            try
            {
                FetchKeyWithSWTToken(audience, issuer);
            }
            catch (InvalidDataContractException ex)
            {
                Assert.IsTrue(ex.Message == "SWT token type template validation error.  template.Issuer is not valid absolute Uri.");
                throw;
            }
        }

        private void FetchKeyWithSWTToken(string audience, string issuer)
        {
            IContentKey contentKey = null;
            IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy = null;
            IContentKeyAuthorizationPolicyOption policyOption = null;

            try
            {
                byte[] expectedKey = null;
                contentKey = CreateTestKey(_mediaContext, ContentKeyType.EnvelopeEncryption, out expectedKey);

                var contentKeyId = Guid.Parse(contentKey.Id.Replace("nb:kid:UUID:", String.Empty));

                TokenRestrictionTemplate tokenRestrictionTemplate = new TokenRestrictionTemplate(TokenType.SWT);
                tokenRestrictionTemplate.PrimaryVerificationKey = new SymmetricVerificationKey();
                    // the default constructor automatically generates a random key

                tokenRestrictionTemplate.Audience = audience;
                tokenRestrictionTemplate.Issuer = issuer;
                tokenRestrictionTemplate.TokenType = TokenType.SWT;
                tokenRestrictionTemplate.RequiredClaims.Add(new TokenClaim(TokenClaim.ContentKeyIdentifierClaimType,
                    contentKeyId.ToString()));

                string optionName = "GetHlsKeyDeliveryUrlAndFetchKeyWithSWTAuthentication";
                string requirements = TokenRestrictionTemplateSerializer.Serialize(tokenRestrictionTemplate);
                ContentKeyRestrictionType restrictionType = ContentKeyRestrictionType.TokenRestricted;
                var _testOption = ContentKeyAuthorizationPolicyOptionTests.CreateOption(_mediaContext, optionName,
                    ContentKeyDeliveryType.BaselineHttp, requirements, null, restrictionType);

                List<IContentKeyAuthorizationPolicyOption> options = new List<IContentKeyAuthorizationPolicyOption>
                {
                    _testOption
                };

                contentKeyAuthorizationPolicy = CreateTestPolicy(_mediaContext, String.Empty, options, ref contentKey);


                Uri keyDeliveryServiceUri = contentKey.GetKeyDeliveryUrl(ContentKeyDeliveryType.BaselineHttp);

                Assert.IsNotNull(keyDeliveryServiceUri);

                Assert.IsTrue(keyDeliveryServiceUri.Host.StartsWith(_mediaContext.Credentials.ClientId));

                KeyDeliveryServiceClient keyClient = new KeyDeliveryServiceClient(RetryPolicy.DefaultFixed);
                string swtTokenString = TokenRestrictionTemplateSerializer.GenerateTestToken(tokenRestrictionTemplate,
                    tokenRestrictionTemplate.PrimaryVerificationKey, contentKeyId, DateTime.Now.AddDays(2));
                byte[] key = keyClient.AcquireHlsKeyWithBearerHeader(keyDeliveryServiceUri, swtTokenString);

                string expectedString = GetString(expectedKey);
                string fetchedString = GetString(key);
                Assert.AreEqual(expectedString, fetchedString);
            }
            finally
            {
                CleanupKeyAndPolicy(contentKey, contentKeyAuthorizationPolicy, policyOption);
            }
        }


        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [ExpectedException(typeof(DataServiceQueryException))]
        public void EnsurePlayReadyLicenseDeliveryUrlForEnvelopeKeyFails()
        {
            IContentKey contentKey = null;
            IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy = null;
            IContentKeyAuthorizationPolicyOption policyOption = null;

            try
            {
                contentKey = CreateTestKey(_mediaContext, ContentKeyType.EnvelopeEncryption);

                policyOption = ContentKeyAuthorizationPolicyOptionTests.CreateOption(_mediaContext, String.Empty, ContentKeyDeliveryType.BaselineHttp, null, null, ContentKeyRestrictionType.Open);

                List<IContentKeyAuthorizationPolicyOption> options = new List<IContentKeyAuthorizationPolicyOption>
                {
                    policyOption
                };

                contentKeyAuthorizationPolicy = CreateTestPolicy(_mediaContext, String.Empty, options, ref contentKey);

                contentKey.GetKeyDeliveryUrl(ContentKeyDeliveryType.PlayReadyLicense);
            }
            finally
            {
                CleanupKeyAndPolicy(contentKey, contentKeyAuthorizationPolicy, policyOption);
            }
        }

        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [ExpectedException(typeof(DataServiceQueryException))]
        public void EnsureEnvelopeKeyDeliveryUrlForCommonKeyFails()
        {
            IContentKey contentKey = null;
            IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy = null;
            IContentKeyAuthorizationPolicyOption policyOption = null;

            PlayReadyLicenseResponseTemplate responseTemplate = new PlayReadyLicenseResponseTemplate();
            responseTemplate.LicenseTemplates.Add(new PlayReadyLicenseTemplate());
            string licenseTemplate = MediaServicesLicenseTemplateSerializer.Serialize(responseTemplate);

            try
            {
                contentKey = CreateTestKey(_mediaContext, ContentKeyType.CommonEncryption);

                policyOption = ContentKeyAuthorizationPolicyOptionTests.CreateOption(_mediaContext, String.Empty, ContentKeyDeliveryType.PlayReadyLicense, null, licenseTemplate, ContentKeyRestrictionType.Open);

                List<IContentKeyAuthorizationPolicyOption> options = new List<IContentKeyAuthorizationPolicyOption>
                {
                    policyOption
                };

                contentKeyAuthorizationPolicy = CreateTestPolicy(_mediaContext, String.Empty, options, ref contentKey);

                Uri keyDeliveryServiceUri = contentKey.GetKeyDeliveryUrl(ContentKeyDeliveryType.BaselineHttp);
            }
            finally
            {
                CleanupKeyAndPolicy(contentKey, contentKeyAuthorizationPolicy, policyOption);
            }
        }

        [TestMethod]
        [TestCategory("ClientSDK")]
        [Owner("ClientSDK")]
        [ExpectedException(typeof(DataServiceQueryException))]
        public void EnsureNoneKeyDeliveryUrlFails()
        {
            IContentKey contentKey = null;
            IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy = null;
            IContentKeyAuthorizationPolicyOption policyOption = null;

            try
            {
                contentKey = CreateTestKey(_mediaContext, ContentKeyType.EnvelopeEncryption);

                policyOption = ContentKeyAuthorizationPolicyOptionTests.CreateOption(_mediaContext, String.Empty, ContentKeyDeliveryType.BaselineHttp, null, null, ContentKeyRestrictionType.Open);

                List<IContentKeyAuthorizationPolicyOption> options = new List<IContentKeyAuthorizationPolicyOption>
                {
                    policyOption
                };

                contentKeyAuthorizationPolicy = CreateTestPolicy(_mediaContext, String.Empty, options, ref contentKey);

                Uri keyDeliveryServiceUri = contentKey.GetKeyDeliveryUrl(ContentKeyDeliveryType.None);
            }
            finally
            {
                CleanupKeyAndPolicy(contentKey, contentKeyAuthorizationPolicy, policyOption);
            }
        }

        #region HelperMethods

        private void CleanupKeyAndPolicy(IContentKey contentKey, IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy, IContentKeyAuthorizationPolicyOption policyOption)
        {
            if (contentKey != null)
            {
                contentKey.Delete();
            }

            if (contentKeyAuthorizationPolicy != null)
            {
                contentKeyAuthorizationPolicy.Delete();
            }

            /*
            if (policyOption != null)
            {
                policyOption.Delete();
            }
            */
        }

        public static IContentKey CreateTestKey(CloudMediaContext mediaContext, ContentKeyType contentKeyType, string name = "")
        {
            byte[] key;
            return CreateTestKey(mediaContext, contentKeyType, out key);
        }

        public static IContentKey CreateTestKey(CloudMediaContext mediaContext, ContentKeyType contentKeyType, out byte[] key, string name = "")
        {
            key = ContentKeyTests.GetRandomBuffer(16);
            IContentKey contentKey = mediaContext.ContentKeys.Create(Guid.NewGuid(), key, name, contentKeyType);

            return contentKey;
        }

        public static IContentKey CreateTestKeyWithSpecified(string keyIdentifier, CloudMediaContext mediaContext, ContentKeyType contentKeyType, string name = "")
        {
            var keyId = EncryptionUtils.GetKeyIdAsGuid(keyIdentifier);
            SymmetricAlgorithm symmetricAlgorithm = new AesCryptoServiceProvider();
            if ((contentKeyType == ContentKeyType.CommonEncryption) ||
                (contentKeyType == ContentKeyType.EnvelopeEncryption))
            {
                symmetricAlgorithm.KeySize = EncryptionUtils.KeySizeInBitsForAes128;
            }
            else
            {
                symmetricAlgorithm.KeySize = EncryptionUtils.KeySizeInBitsForAes256;
            }
            IContentKey contentKey = mediaContext.ContentKeys.Create(keyId, symmetricAlgorithm.Key, name, contentKeyType);

            return contentKey;
        }

        public static Guid GetGuidFromBase64String(string base64keyId)
        {
            byte[] keyIdBytes = Convert.FromBase64String(base64keyId);
            Guid keyId = new Guid(keyIdBytes);
            return keyId;
        }

        public static IContentKeyAuthorizationPolicy CreateTestPolicy(CloudMediaContext mediaContext, string name, List<IContentKeyAuthorizationPolicyOption> policyOptions, ref IContentKey contentKey)
        {
            IContentKeyAuthorizationPolicy contentKeyAuthorizationPolicy = mediaContext.ContentKeyAuthorizationPolicies.CreateAsync(name).Result;

            foreach (IContentKeyAuthorizationPolicyOption option in policyOptions)
            {
                contentKeyAuthorizationPolicy.Options.Add(option);
            }

            // Associate the content key authorization policy with the content key
            contentKey.AuthorizationPolicyId = contentKeyAuthorizationPolicy.Id;
            contentKey = contentKey.UpdateAsync().Result;

            return contentKeyAuthorizationPolicy;
        }


        public static string GetString(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }
            char[] chars = new char[bytes.Length / sizeof(char)];
            Buffer.BlockCopy(bytes, 0, chars, 0, bytes.Length);
            return new string(chars);
        }

        #endregion // HelperMethods
    }
}
