# Actual package filename admission

Initial data-only Prepare failed before creating a runtime: the intact published package contains `Source/obj/Debug/.NETFramework,Version=v4.7.2.AssemblyAttributes.cs`, a valid Windows filename containing `=`. The reused controller's canonical-path helper rejected it.

Root and independent reviewer inspected all relevant filesystem and command boundaries. Removing only the equals-sign ban is appropriate: absolute-path validation, quote rejection, GetFullPath, containment, reparse and hash checks remain. Package filenames use literal-path/.NET file APIs. Metadata paths are Base64 data in fixed encoded command text; game command arguments use generated run paths, not the package's source filenames. No published file is discarded or rewritten.

The original controller ADD8633C is preserved in controller-before-equals.ps1.txt. Corrected controller SHA25634A40853A5893F8FAEC4986B252B5700B5996CF4AED0807CA223C1A4E5369092 parses without errors. Product pair, scenario, host, provider and launcher are unchanged. A fresh Prepare is required; no native run occurred for the refusal.
