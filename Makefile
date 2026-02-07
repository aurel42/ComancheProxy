.PHONY: build run clean all

all: build

build:
	dotnet build

run:
	dotnet run

clean:
	dotnet clean
