using System.Text;

namespace CyRevision.Core.Projects;

public sealed record ProjectLicenseTemplate(
    string Id,
    string Name,
    string Description,
    string Content,
    string Permissions = "Review the complete terms before distribution.",
    string Conditions = "Preserve the license and copyright notices.",
    string Limitations = "No warranty is provided.",
    string ReferenceUrl = "https://spdx.org/licenses/");

public sealed record ProjectLicenseSnapshot(
    bool Exists,
    string FileName,
    string FullPath,
    string Content,
    string DetectedTemplateId,
    long Size,
    DateTimeOffset? LastModifiedAt);

public sealed class ProjectLicenseService
{
    private const int MaximumLicenseBytes = 2 * 1024 * 1024;
    private static readonly string[] PreferredFileNames =
    [
        "LICENSE", "LICENSE.md", "LICENSE.txt", "COPYING", "COPYING.md", "COPYING.txt"
    ];

    public IReadOnlyList<ProjectLicenseTemplate> Templates { get; } =
    [
        new("MIT", "MIT", "Permissive license with attribution.", MitTemplate,
            "Commercial use, modification, distribution and private use.",
            "Keep the copyright and license notices.", "No liability and no warranty.",
            "https://spdx.org/licenses/MIT.html"),
        new("BSD-3-Clause", "BSD 3-Clause", "Permissive license with a non-endorsement clause.", BsdThreeClauseTemplate,
            "Commercial use, modification, distribution and private use.",
            "Keep notices; do not use contributor names for endorsement.", "No liability and no warranty.",
            "https://spdx.org/licenses/BSD-3-Clause.html"),
        new("0BSD", "BSD Zero Clause", "Very short permissive license without an attribution condition.", ZeroBsdTemplate,
            "Use, copy, modify and distribute for any purpose.", "No attribution condition in the license text.",
            "No liability and no warranty.", "https://spdx.org/licenses/0BSD.html"),
        new("BSL-1.0", "Boost Software License 1.0", "Permissive license commonly used by C++ libraries.", BoostTemplate,
            "Use, reproduce, display, distribute, execute and create derivatives.",
            "Keep the notices in source copies; compiled machine-executable forms are exempt.",
            "No liability and no warranty.", "https://spdx.org/licenses/BSL-1.0.html"),
        new("Zlib", "zlib License", "Compact permissive license suitable for source and binary distribution.", ZlibTemplate,
            "Use, alter and redistribute, including commercially.",
            "Do not misrepresent authorship; mark altered versions; preserve the notice.",
            "No liability and no warranty.", "https://spdx.org/licenses/Zlib.html"),
        new("PostgreSQL", "PostgreSQL License", "Short permissive license derived from BSD/MIT-style terms.", PostgreSqlTemplate,
            "Use, copy, modify and distribute for any purpose without fee.",
            "Keep the copyright and permission notices.", "No liability and no warranty.",
            "https://spdx.org/licenses/PostgreSQL.html"),
        new("ISC", "ISC", "Short permissive license similar to MIT.", IscTemplate,
            "Use, copy, modify and distribute for any purpose with or without fee.",
            "Keep the copyright and permission notices.", "No liability and no warranty.",
            "https://spdx.org/licenses/ISC.html"),
        new("Unlicense", "Unlicense", "Public-domain dedication with a warranty disclaimer.", UnlicenseTemplate,
            "Unrestricted use, copying, modification and distribution.",
            "Public-domain dedication; local law may affect its interpretation.",
            "No liability and no warranty.", "https://spdx.org/licenses/Unlicense.html"),
        new("Proprietary", "Proprietary / All rights reserved", "Starter notice for closed-source projects; review it with legal counsel.", ProprietaryTemplate,
            "Only the rights explicitly granted by the owner.",
            "Distribution and modification require written authorization.",
            "The starter notice is not a substitute for legal advice.", string.Empty),
        new("Custom", "Custom", "Edit or paste the complete license text manually.", string.Empty,
            "Defined by your custom terms.", "Defined by your custom terms.",
            "Have custom terms reviewed before distribution.", string.Empty)
    ];

    public async Task<ProjectLicenseSnapshot> InspectAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        string root = ValidateProjectRoot(projectRoot);
        string? path = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(candidate => IsLicenseFileName(Path.GetFileName(candidate)))
            .OrderBy(candidate => LicenseFilePriority(Path.GetFileName(candidate)))
            .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (path is null)
        {
            return new ProjectLicenseSnapshot(
                false,
                "LICENSE",
                Path.Combine(root, "LICENSE"),
                string.Empty,
                "Custom",
                0,
                null);
        }

        FileInfo info = new(path);
        if (info.Length > MaximumLicenseBytes)
            throw new InvalidOperationException($"The license file is larger than {MaximumLicenseBytes / 1024:N0} KiB.");
        string content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return new ProjectLicenseSnapshot(
            true,
            info.Name,
            info.FullName,
            content,
            DetectTemplateId(content),
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    public string RenderTemplate(
        string templateId,
        string holder,
        int year,
        string projectName)
    {
        ProjectLicenseTemplate template = Templates.FirstOrDefault(item =>
            string.Equals(item.Id, templateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Select a supported license template.");
        string normalizedHolder = string.IsNullOrWhiteSpace(holder) ? projectName.Trim() : holder.Trim();
        if (template.Id == "Custom")
        {
            return $"{projectName.Trim()} license\n\nReplace this text with the complete license terms for this project.\n";
        }

        return template.Content
            .Replace("{year}", year.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{holder}", normalizedHolder, StringComparison.Ordinal)
            .Replace("{project}", projectName.Trim(), StringComparison.Ordinal)
            .Trim() + Environment.NewLine;
    }

    public async Task SaveAsync(
        string projectRoot,
        string fileName,
        string content,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        string root = ValidateProjectRoot(projectRoot);
        string safeFileName = ValidateFileName(fileName);
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("The license text cannot be empty.");
        string target = Path.Combine(root, safeFileName);
        if (File.Exists(target) && !overwrite)
            throw new IOException($"{safeFileName} already exists.");

        string temporary = Path.Combine(root, $".cyrevision-license-{Guid.NewGuid():N}.tmp");
        try
        {
            string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .TrimEnd() + Environment.NewLine;
            await File.WriteAllTextAsync(temporary, normalized, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, target, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<string> ReadDraftAsync(string path, CancellationToken cancellationToken = default)
    {
        FileInfo info = new(Path.GetFullPath(path));
        if (!info.Exists) throw new FileNotFoundException("The selected license file was not found.", info.FullName);
        if (info.Length > MaximumLicenseBytes)
            throw new InvalidOperationException($"The selected file is larger than {MaximumLicenseBytes / 1024:N0} KiB.");
        return await File.ReadAllTextAsync(info.FullName, cancellationToken).ConfigureAwait(false);
    }

    public static string DetectTemplateId(string content)
    {
        if (content.Contains("GNU AFFERO GENERAL PUBLIC LICENSE", StringComparison.OrdinalIgnoreCase)) return "AGPL-3.0";
        if (content.Contains("GNU GENERAL PUBLIC LICENSE", StringComparison.OrdinalIgnoreCase)) return "GPL-3.0";
        if (content.Contains("Apache License", StringComparison.OrdinalIgnoreCase) &&
            content.Contains("Version 2.0", StringComparison.OrdinalIgnoreCase)) return "Apache-2.0";
        if (content.Contains("Mozilla Public License Version 2.0", StringComparison.OrdinalIgnoreCase)) return "MPL-2.0";
        if (content.Contains("Boost Software License - Version 1.0", StringComparison.OrdinalIgnoreCase)) return "BSL-1.0";
        if (content.Contains("PostgreSQL Database Management System", StringComparison.OrdinalIgnoreCase)) return "PostgreSQL";
        if (content.Contains("This software is provided 'as-is'", StringComparison.OrdinalIgnoreCase) &&
            content.Contains("altered source versions", StringComparison.OrdinalIgnoreCase)) return "Zlib";
        if (content.Contains("AUTHOR DISCLAIMS ALL WARRANTIES", StringComparison.OrdinalIgnoreCase) &&
            content.Contains("PERFORMANCE OF THIS SOFTWARE", StringComparison.OrdinalIgnoreCase)) return "0BSD";
        if (content.Contains("Permission is hereby granted, free of charge", StringComparison.OrdinalIgnoreCase)) return "MIT";
        if (content.Contains("Neither the name of", StringComparison.OrdinalIgnoreCase)) return "BSD-3-Clause";
        if (content.Contains("Permission to use, copy, modify, and/or distribute", StringComparison.OrdinalIgnoreCase)) return "ISC";
        if (content.Contains("free and unencumbered software released into the public domain", StringComparison.OrdinalIgnoreCase)) return "Unlicense";
        if (content.Contains("All rights reserved", StringComparison.OrdinalIgnoreCase)) return "Proprietary";
        return "Custom";
    }

    public static string ValidateFileName(string fileName)
    {
        string value = string.IsNullOrWhiteSpace(fileName) ? "LICENSE" : fileName.Trim();
        if (!string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("The license file name must be a file in the project root.");
        return value;
    }

    private static string ValidateProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new InvalidOperationException("Select a project first.");
        string root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return root;
    }

    private static bool IsLicenseFileName(string fileName)
    {
        string upper = fileName.ToUpperInvariant();
        return upper is "LICENSE" or "LICENSE.MD" or "LICENSE.TXT" or
               "COPYING" or "COPYING.MD" or "COPYING.TXT" or "LICENCE" or "LICENCE.MD" or "LICENCE.TXT";
    }

    private static int LicenseFilePriority(string fileName)
    {
        int index = Array.FindIndex(PreferredFileNames, candidate =>
            string.Equals(candidate, fileName, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private const string MitTemplate = """
Copyright (c) {year} {holder}

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
""";

    private const string BsdThreeClauseTemplate = """
Copyright (c) {year}, {holder}
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
""";

    private const string ZeroBsdTemplate = """
Copyright (C) {year} by {holder}

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THIS SOFTWARE.
""";

    private const string BoostTemplate = """
Boost Software License - Version 1.0 - August 17th, 2003

Permission is hereby granted, free of charge, to any person or organization
obtaining a copy of the software and accompanying documentation covered by
this license (the "Software") to use, reproduce, display, distribute,
execute, and transmit the Software, and to prepare derivative works of the
Software, and to permit third-parties to whom the Software is furnished to
do so, all subject to the following:

The copyright notices in the Software and this entire statement, including
the above license grant, this restriction and the following disclaimer,
must be included in all copies of the Software, in whole or in part, and all
derivative works of the Software, unless such copies or derivative works are
solely in the form of machine-executable object code generated by a source
language processor.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
""";

    private const string ZlibTemplate = """
Copyright (c) {year} {holder}

This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from the
use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not claim
   that you wrote the original software. If you use this software in a
   product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
""";

    private const string PostgreSqlTemplate = """
PostgreSQL Database Management System
(formerly known as Postgres, then as Postgres95)

Portions Copyright (c) {year}, {holder}

Permission to use, copy, modify, and distribute this software and its
documentation for any purpose, without fee, and without a written agreement
is hereby granted, provided that the above copyright notice and this
paragraph and the following two paragraphs appear in all copies.

IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR DIRECT,
INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES, INCLUDING LOST
PROFITS, ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING, BUT
NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
PARTICULAR PURPOSE. THE SOFTWARE PROVIDED HEREUNDER IS ON AN "AS IS" BASIS,
AND THE COPYRIGHT HOLDER HAS NO OBLIGATIONS TO PROVIDE MAINTENANCE, SUPPORT,
UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
""";

    private const string IscTemplate = """
Copyright (c) {year} {holder}

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY
AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM
LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR
OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR
PERFORMANCE OF THIS SOFTWARE.
""";

    private const string UnlicenseTemplate = """
This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or distribute this
software, either in source code form or as a compiled binary, for any purpose,
commercial or non-commercial, and by any means.

In jurisdictions that recognize copyright laws, the author or authors of this
software dedicate any and all copyright interest in the software to the public
domain. We make this dedication for the benefit of the public at large and to
the detriment of our heirs and successors. We intend this dedication to be an
overt act of relinquishment in perpetuity of all present and future rights to
this software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
""";

    private const string ProprietaryTemplate = """
Copyright (c) {year} {holder}. All rights reserved.

{project} is proprietary and confidential software. Unauthorized copying,
modification, distribution, publication, or use of this software, in whole or
in part, is prohibited without prior written permission from the copyright
holder.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED. Review these terms with qualified legal counsel before distribution.
""";
}
