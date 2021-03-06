﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// 

using System.ServiceModel;

using Infrastructure.Common;

using Xunit;

public static class Http_ClientCredentialTypeTests
{
    [Fact]
    [OuterLoop]
    [ActiveIssue(1045)]
    public static void DigestAuthentication_Echo_RoundTrips_String_No_Domain()
    {
        ChannelFactory<IWcfService> factory = null;
        IWcfService serviceProxy = null;
        string testString = "Hello";
        BasicHttpBinding binding;

        try
        {
            // *** SETUP *** \\
            binding = new BasicHttpBinding(BasicHttpSecurityMode.TransportCredentialOnly);
            binding.Security.Transport.ClientCredentialType = HttpClientCredentialType.Digest;
            factory = new ChannelFactory<IWcfService>(binding, new EndpointAddress(Endpoints.Http_DigestAuth_NoDomain_Address));

            // https://github.com/dotnet/wcf/issues/1045 needs to supply a replacement for NetworkCredential below
            //factory.Credentials.HttpDigest.ClientCredential = BridgeClientAuthenticationManager.NetworkCredential;

            serviceProxy = factory.CreateChannel();

            // *** EXECUTE *** \\
            string result = serviceProxy.Echo(testString);

            // *** VALIDATE *** \\
            Assert.True(result == testString, string.Format("Error: expected response from service: '{0}' Actual was: '{1}'", testString, result));

            // *** CLEANUP *** \\
            factory.Close();
            ((ICommunicationObject)serviceProxy).Close();
        }
        finally
        {
            // *** ENSURE CLEANUP *** \\
            ScenarioTestHelpers.CloseCommunicationObjects((ICommunicationObject)serviceProxy, factory);
        }
    }
}
