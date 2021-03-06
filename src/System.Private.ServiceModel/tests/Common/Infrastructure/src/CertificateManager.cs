﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Security.Cryptography; 
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Infrastructure.Common
{
    // This class manages the adding and removal of certificates.
    // It also handles informing http.sys of which certificate to associate with SSL ports.
    internal static class CertificateManager
    {
        private static object s_certificateLock = new object();
        private static bool s_PlatformSpecificStoreLocationIsSet = false;
        private static StoreLocation s_PlatformSpecificRootStoreLocation = StoreLocation.LocalMachine;

        // The location from where to open StoreName.Root
        //
        // Since we don't have access to System.Runtime.InteropServices.IsOSPlatform API, we need to find a novel way to switch
        // the store we use on Linux
        //
        // For Linux: 
        // On Linux, opening stores from the LocalMachine store is not supported yet, so we toggle to use CurrentUser
        // Furthermore, we don't need to sudo in Linux to install a StoreName.Root : StoreLocation.CurrentUser. So this will allow
        // tests requiring certs to pass in Linux. 
        // See dotnet/corefx#3690
        //
        // For Windows: 
        // We don't want to use CurrentUser, as writing to StoreName.Root : StoreLocation.CurrentUser will result in a 
        // modal dialog box that isn't dismissable if the root cert hasn't already been installed previously. 
        // If the cert has already been installed, writing to StoreName.Root : StoreLocation.CurrentUser results in a no-op
        // 
        // In other words, on Windows, we can bypass the modal dialog box, but only if we install to StoreName.Root : StoreLocation.LocalMachine
        // To do this though means that we must run certificate-based tests elevated
        private static StoreLocation PlatformSpecificRootStoreLocation
        {
            get
            {
                if (!s_PlatformSpecificStoreLocationIsSet)
                {
                    try
                    {
                        using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                        {
                            store.Open(OpenFlags.ReadWrite);
                        }
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // Linux
                        s_PlatformSpecificRootStoreLocation = StoreLocation.CurrentUser; 
                    }
                    s_PlatformSpecificStoreLocationIsSet = true;
                }
                return s_PlatformSpecificRootStoreLocation;
            }
        }

        // Adds the given certificate to the given store unless it is
        // already present.  Returns the  certificate either already in
        // the store or the one requested.
        public static X509Certificate2 AddToStoreIfNeeded(StoreName storeName,
                                                          StoreLocation storeLocation,
                                                          X509Certificate2 certificate)
        {
            X509Certificate2 resultCert = null;
            lock (s_certificateLock)
            {
                // Open the store as ReadOnly first, as it prevents the need for elevation if opening
                // a LocalMachine store
                using (X509Store store = new X509Store(storeName, storeLocation))
                {
                    store.Open(OpenFlags.ReadOnly);
                    resultCert = CertificateFromThumbprint(store, certificate.Thumbprint);
                }

                // Not already in store.  We need to add it.
                if (resultCert == null)
                {
                    using (X509Store store = new X509Store(storeName, storeLocation))
                    {
                        try
                        {
                            store.Open(OpenFlags.ReadWrite);
                            store.Add(certificate);
                            resultCert = certificate;
                        }
                        catch (CryptographicException inner)
                        {
                            StringBuilder exceptionString = new StringBuilder();
                            exceptionString.AppendFormat("Error opening StoreName: '{0}' certificate store from StoreLocation '{1}' in ReadWrite mode ", storeName, storeLocation);
                            exceptionString.AppendFormat("while attempting to install cert with thumbprint '{1}'.", Environment.NewLine, certificate.Thumbprint);
                            exceptionString.AppendFormat("{0}This is usually due to permissions issues if writing to the LocalMachine location", Environment.NewLine);
                            exceptionString.AppendFormat("{0}Try running the test with elevated or superuser permissions.", Environment.NewLine);

                            throw new InvalidOperationException(exceptionString.ToString(), inner);
                        }
                    }
                }
            }

            return resultCert;
        }

        // Returns the certificate matching the given thumbprint from the given store.
        // Returns null if not found.
        private static X509Certificate2 CertificateFromThumbprint(X509Store store, string thumbprint)
        {
            X509Certificate2Collection foundCertificates = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: true);
            return foundCertificates.Count == 0 ? null : foundCertificates[0];
        }

        private static X509Certificate2 CertificateFromThumbprint(StoreName storeName,
                                                                  StoreLocation storeLocation,
                                                                  string thumbprint)
        {
            X509Certificate2 resultCert = null;
            using (X509Store store = new X509Store(storeName, storeLocation))
            {
                store.Open(OpenFlags.ReadOnly);
                resultCert = CertificateFromThumbprint(store, thumbprint);
            }

            return resultCert;
        }

        // Retrieves a root certificate matching the given thumbprint from the root store
        public static X509Certificate2 RootCertificateFromThumprint(string thumbprint)
        {
            return CertificateFromThumbprint(StoreName.Root, PlatformSpecificRootStoreLocation, thumbprint);
        }

        // Retrieves a root certificate matching the given issuer name and subject name from the root store
        public static X509Certificate2 RootCertificateFromName(string issuerName, string subjectName)
        {
            return CertificateFromName(StoreName.Root, PlatformSpecificRootStoreLocation, issuerName, subjectName);
        }

        // Retrieves a client certificate matching the given thumbprint from the certificate store
        public static X509Certificate2 ClientCertificateFromThumprint(string thumbprint)
        {
            return CertificateFromThumbprint(StoreName.My, StoreLocation.CurrentUser, thumbprint);
        }

        // Retrieves a client certificate matching the given issuer name and subject name from the certificate store
        public static X509Certificate2 ClientCertificateFromName(string issuerName, string subjectName)
        {
            return CertificateFromName(StoreName.My, StoreLocation.CurrentUser, issuerName, subjectName);
        }

        private static X509Certificate2 CertificateFromName(StoreName storeName,
                                                            StoreLocation storeLocation,
                                                            string issuerName,
                                                            string subjectName = null)
        {
            using (X509Store store = new X509Store(storeName, storeLocation))
            {
                try
                {
                    store.Open(OpenFlags.ReadOnly);
                    X509Certificate2Collection certificates =
                        store.Certificates.Find(X509FindType.FindByIssuerName, issuerName, validOnly: true);
                    if (certificates.Count == 0)
                    {
                        return null;
                    }

                    if (subjectName != null)
                    {
                        certificates = certificates.Find(X509FindType.FindBySubjectName, subjectName, validOnly: true);
                    }

                    return certificates.Count == 0 ? null : certificates[0];
                }
                catch
                {
                    return null;
                }
            }
        }


        // Install the certificate into the Root store and returns its thumbprint.
        // It will not install the certificate if it is already present in the store.
        // It returns the thumbprint of the certificate, regardless whether it was added or found.
        public static X509Certificate2 InstallCertificateToRootStore(X509Certificate2 certificate)
        {
            // See explanation of StoreLocation selection at PlatformSpecificRootStoreLocation
            certificate = AddToStoreIfNeeded(StoreName.Root, PlatformSpecificRootStoreLocation, certificate);
            return certificate;
        }

        // Install the certificate into the My store.
        // It will not install the certificate if it is already present in the store.
        // It returns the thumbprint of the certificate, regardless whether it was added or found.
        public static X509Certificate2 InstallCertificateToMyStore(X509Certificate2 certificate)
        {
            // Always install client certs to CurrentUser
            // StoreLocation.CurrentUser is supported on both Linux and Windows 
            // Furthermore, installing this cert to this location does not require sudo or admin elevation
            certificate = AddToStoreIfNeeded(StoreName.My, StoreLocation.CurrentUser, certificate);

            return certificate;
        }
    }
}
