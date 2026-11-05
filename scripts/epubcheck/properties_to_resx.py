#!/usr/bin/env python3
"""Converts a Java .properties message bundle of EPUBCheck into a .resx file.

Usage: properties_to_resx.py <in.properties> <out.resx> [--reference <ref.properties>] [--keep-leading-space KEY ...]

The placeholders (%1$s) stay as they are: the custom messages files of EPUBCheck use them, and the texts stay
comparable with upstream's. The properties are read as Java reads them (comments, '=' / ':' / white space as the
separator, white space before the value dropped, trailing white space kept, backslash escapes, line continuation),
except for the keys named by --keep-leading-space, whose value keeps the white space before it (upstream's
PKG_007.whitespace starts with a space that Java drops, so its sentence is glued to the one before it).

With --reference the keys of the input must be exactly those of the reference and each text must use the same
placeholders (used to check the translations).
"""
import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape

HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""

ESCAPES = {"n": "\n", "t": "\t", "r": "\r", "f": "\f"}


def unescape(text: str) -> str:
    out, i = [], 0
    while i < len(text):
        c = text[i]
        if c == "\\" and i + 1 < len(text):
            n = text[i + 1]
            if n == "u":
                out.append(chr(int(text[i + 2:i + 6], 16)))
                i += 6
                continue
            out.append(ESCAPES.get(n, n))
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def parse(path: Path, keep_leading_space) -> dict:
    result = {}
    pending = None  # the start of a logical line that continues on the next physical line
    for raw in path.read_text(encoding="utf-8").split("\n"):
        if pending is None:
            line = raw.lstrip(" \t\f")
            if not line or line[0] in "#!":
                continue
        else:
            line = pending + raw.lstrip(" \t\f")
        trailing = len(line) - len(line.rstrip("\\"))
        if trailing % 2 == 1:  # an odd number of trailing backslashes: the line continues
            pending = line[:-1]
            continue
        pending = None
        # The key ends at the first unescaped '=', ':' or white space; white space around the separator is dropped.
        m = re.match(r"((?:\\.|[^=:\s\\])+)(\s*[=:]\s*|\s+)?(.*)$", line, re.S)
        key, sep, value = unescape(m.group(1)), m.group(2) or "", m.group(3)
        if key in keep_leading_space and "=" in sep:
            value = sep[sep.index("=") + 1:] + value
        result[key] = unescape(value)
    return result


def placeholders(text: str):
    return sorted(re.findall(r"%\d+\$[sd]", text))


def main() -> None:
    args = sys.argv[1:]
    src, dst = Path(args[0]), Path(args[1])
    reference = None
    keep = set()
    i = 2
    while i < len(args):
        if args[i] == "--reference":
            reference = Path(args[i + 1])
            i += 2
        elif args[i] == "--keep-leading-space":
            keep.update(args[i + 1:])
            break
        else:
            raise SystemExit("unknown argument " + args[i])
    data = parse(src, keep)
    if reference is not None:
        ref = parse(reference, keep)
        missing, extra = sorted(set(ref) - set(data)), sorted(set(data) - set(ref))
        if missing or extra:
            raise SystemExit(f"keys differ from the reference: missing {missing}, extra {extra}")
        bad = [k for k in ref if placeholders(ref[k]) != placeholders(data[k])]
        if bad:
            raise SystemExit(f"placeholders differ from the reference for {bad}")
    parts = [HEADER]
    for key, value in data.items():
        parts.append(f'  <data name="{escape(key)}" xml:space="preserve">\n    <value>{escape(value)}</value>\n  </data>\n')
    parts.append("</root>\n")
    dst.write_text("".join(parts), encoding="utf-8")
    print(f"{len(data)} keys -> {dst}")


main()
