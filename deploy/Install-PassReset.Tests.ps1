# Pester tests for Install-PassReset.ps1.
# Run: pwsh -NoProfile -Command "Invoke-Pester -Path deploy/Install-PassReset.Tests.ps1 -Output Detailed"
#
# These tests exercise the installer in "dry-run" / function-extraction mode. We
# dot-source the installer to load its functions without executing its top-level
# install flow. The installer's top-level script block checks for the
# $PASSRESET_TEST_MODE env var and short-circuits when set.

BeforeAll {
    $env:PASSRESET_TEST_MODE = '1'
    . "$PSScriptRoot/Install-PassReset.ps1"
}

AfterAll {
    Remove-Item Env:PASSRESET_TEST_MODE -ErrorAction SilentlyContinue
}

Describe 'Install-PassReset: -HostingMode param' {
    It 'accepts IIS' {
        { Test-HostingModeValue -HostingMode 'IIS' } | Should -Not -Throw
    }
    It 'accepts Service' {
        { Test-HostingModeValue -HostingMode 'Service' } | Should -Not -Throw
    }
    It 'accepts Console' {
        { Test-HostingModeValue -HostingMode 'Console' } | Should -Not -Throw
    }
    It 'rejects unknown values' {
        { Test-HostingModeValue -HostingMode 'Nonsense' } | Should -Throw
    }
}

Describe 'Install-PassReset: Test-ServiceModePreflight' {
    It 'returns $false when cert thumbprint is empty' {
        Test-ServiceModePreflight -CertThumbprint '' -Port 443 -ServiceAccount 'NT SERVICE\PassReset' |
            Should -BeFalse
    }
    It 'returns $false when Port is already bound' {
        # Bind a TCP listener on a free high port, then assert preflight fails.
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        try {
            $port = ($listener.LocalEndpoint).Port
            Test-ServiceModePreflight -CertThumbprint 'ABCDEF' -Port $port -ServiceAccount 'NT SERVICE\PassReset' |
                Should -BeFalse
        } finally {
            $listener.Stop()
        }
    }
}
