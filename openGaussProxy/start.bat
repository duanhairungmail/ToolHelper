@echo off
java -cp ".;lib\opengauss-jdbc-5.0.0.jar" OpenGaussProxy %1 %2 %3 %4
@echo off
java -cp ".;lib\postgresql-42.7.4.jar" OpenGaussProxy %1 %2 %3 %4
