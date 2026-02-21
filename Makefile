SOURCES := $(shell find . -name "*.cs")

.PHONY: run debug clean all

all: build

build: $(SOURCES)
	dotnet build

run: build
	dotnet run

debug: build
	dotnet run -- --debug

clean:
	dotnet clean
