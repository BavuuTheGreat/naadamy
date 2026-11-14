#!/bin/zsh
# Compile-checks the Unity runtime scripts (not Editor/) with Unity's own Roslyn. Prints errors only; last line "COMPILE OK" or "COMPILE FAIL n".
R=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Resources/Scripting
REPO=$(cd "$(dirname "$0")/../.." && pwd)
OUT=$(mktemp -d)
refs=()
for d in $R/Managed/UnityEngine/*.dll $R/NetStandard/ref/2.1.0/netstandard.dll; do refs+=("-r:$d"); done
srcs=($(find $REPO/unity/Assets -name "*.cs" -not -path "*/Editor/*"))
$R/NetCoreRuntime/dotnet $R/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll -nologo -noconfig -nostdlib -target:library -langversion:9.0 -nowarn:CS0649,CS0414,CS0169,CS0219,CS0168,CS0618,CS1701,CS1702 -unsafe "-define:${DEFINES:-UNITY_6000_0_OR_NEWER;UNITY_IOS}" -out:$OUT/a.dll $refs $srcs 2>&1 | grep -E "error" | sed "s#$REPO/##" | sort -u > $OUT/err.txt
n=$(wc -l < $OUT/err.txt | tr -d ' ')
head -40 $OUT/err.txt
[ "$n" = 0 ] && echo "COMPILE OK" || echo "COMPILE FAIL $n"
