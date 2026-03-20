SOURCES := $(shell find . -name "*.cs")
VERSION := $(shell awk -F'[<>]' '/<Version>/{print $$3}' ComancheProxy.csproj | tr -d '\r')

.PHONY: run debug clean all dist

all: build

build: $(SOURCES)
	dotnet build

run: build
	dotnet run

debug: build
	dotnet run -- --debug

clean:
	dotnet clean

dist:
	mkdir -p dist dist/tmp
	dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/tmp
	sleep 5 # Thanks, Bill Gates
	powershell -Command 'Compress-Archive -Path dist/tmp/ComancheProxy.exe,config.json,README.md,CHANGELOG.md -DestinationPath dist/ComancheProxy-$(VERSION)-windows-x64.zip -Force'
	rm -rf dist/tmp
