SOURCES := $(shell find . -name "*.cs")
VERSION := $(shell awk -F'[<>]' '/<Version>/{print $$3}' ComancheProxy.csproj | tr -d '\r')

.PHONY: run debug clean all release-binary

all: build

build: $(SOURCES)
	dotnet build

run: build
	dotnet run

debug: build
	dotnet run -- --debug

clean:
	dotnet clean

release-binary:
	mkdir -p release publish-temp
	dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-temp
	powershell -Command "Compress-Archive -Path publish-temp/ComancheProxy.exe,publish-temp/config.json,README.md,CHANGELOG.md -DestinationPath release/ComancheProxy-$(VERSION)-windows-x64.zip -Force"
	rm -rf publish-temp
