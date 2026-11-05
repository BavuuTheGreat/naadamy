# The game calls these from C# by name (AndroidJavaClass, AndroidJavaProxy): a minified build must keep them.
# Credential Manager and Google ID ship their own consumer rules.
-keep class com.pineconeacademy.naadamy.onetap.** { *; }
