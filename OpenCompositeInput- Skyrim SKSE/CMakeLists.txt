cmake_minimum_required(VERSION 3.21)

project(
    OpenCompositeInput
    VERSION 1.0.0
    DESCRIPTION "SKSE VR plugin for OpenComposite - bridges keyboard input to Scaleform"
    LANGUAGES CXX
)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Strip absolute source paths from PDBs so crash logs don't expose local user paths.
if(MSVC)
    string(APPEND CMAKE_CXX_FLAGS " /d1trimfile:\"${CMAKE_SOURCE_DIR}/\"")
    set(CMAKE_SHARED_LINKER_FLAGS_RELEASE "${CMAKE_SHARED_LINKER_FLAGS_RELEASE} /PDBALTPATH:%_PDB%")
    set(CMAKE_SHARED_LINKER_FLAGS_RELWITHDEBINFO "${CMAKE_SHARED_LINKER_FLAGS_RELWITHDEBINFO} /PDBALTPATH:%_PDB%")
endif()

# Force VR-only build for CommonLibSSE (no SE/AE)
set(BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(ENABLE_SKYRIM_SE OFF CACHE BOOL "" FORCE)
set(ENABLE_SKYRIM_AE OFF CACHE BOOL "" FORCE)
set(ENABLE_SKYRIM_VR ON CACHE BOOL "" FORCE)

# Locate CommonLibVR without embedding a developer-specific absolute path.
set(COMMONLIBSSE_ROOT "" CACHE PATH "Path to the CommonLibSSE-NG/CommonLibVR source tree")
if(NOT COMMONLIBSSE_ROOT AND DEFINED ENV{COMMONLIBSSE_ROOT})
    set(COMMONLIBSSE_ROOT "$ENV{COMMONLIBSSE_ROOT}")
endif()
if(NOT COMMONLIBSSE_ROOT)
    get_filename_component(
        COMMONLIBSSE_ROOT
        "${CMAKE_CURRENT_SOURCE_DIR}/../../VR-Project-Staging/external/commonlibsse-ng"
        ABSOLUTE
    )
endif()
if(NOT EXISTS "${COMMONLIBSSE_ROOT}/CMakeLists.txt")
    message(FATAL_ERROR
        "CommonLibSSE-NG/CommonLibVR was not found. Set COMMONLIBSSE_ROOT "
        "with -DCOMMONLIBSSE_ROOT=<path> or the COMMONLIBSSE_ROOT environment variable."
    )
endif()

# Add CommonLibVR as a subdirectory
add_subdirectory(
    "${COMMONLIBSSE_ROOT}"
    CommonLibSSE
    EXCLUDE_FROM_ALL
)

# Include the plugin helper macro from CommonLibVR
include("${COMMONLIBSSE_ROOT}/cmake/CommonLibSSE.cmake")

# Plugin source files
set(SOURCES
    src/Main.cpp
)

# Create the SKSE plugin DLL
add_commonlibsse_plugin(${PROJECT_NAME}
    SOURCES ${SOURCES}
    NAME "OpenCompositeInput"
    AUTHOR "Wondernuttz"
    VERSION ${PROJECT_VERSION}
    USE_ADDRESS_LIBRARY
)

# Use our wrapper PCH (includes CommonLibVR's PCH + global using declarations)
target_precompile_headers(${PROJECT_NAME}
    PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/src/PCH.h"
)

# MSVC-specific options for the plugin
if(MSVC)
    target_compile_options(${PROJECT_NAME} PRIVATE
        /permissive-
        /Zc:preprocessor
        /EHsc
        /W4
        /bigobj
    )
endif()

set_target_properties(${PROJECT_NAME} PROPERTIES
    OUTPUT_NAME "OpenCompositeInput"
    SUFFIX ".dll"
)
